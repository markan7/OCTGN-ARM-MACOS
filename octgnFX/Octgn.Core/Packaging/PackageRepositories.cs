// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Octgn.Core.Packaging
{
    /// <summary>A game feed: a NuGet feed over HTTP (V2 or V3) or a local folder of .nupkg files.</summary>
    public interface IPackageRepository
    {
        string Source { get; }

        /// <summary>Packages of the feed. Network errors are reported as <see cref="WebException"/>.</summary>
        IEnumerable<IPackage> GetPackages();

        /// <summary>Latest versions of the given package ids. Network errors are reported as <see cref="WebException"/>.</summary>
        IEnumerable<IPackage> FindPackages(IEnumerable<string> packageIds);
    }

    /// <summary>Creates repositories the way NuGet.Core did: http(s) sources are feeds, anything else a folder.</summary>
    public sealed class PackageRepositoryFactory
    {
        public static PackageRepositoryFactory Default { get; } = new PackageRepositoryFactory();

        /// <summary>Returns the credentials to use for a feed, or null. Set by <see cref="GameFeedManager"/>.</summary>
        public static Func<Uri, NetworkCredential> CredentialProvider { get; set; }

        public IPackageRepository CreateRepository(string source) {
            if (string.IsNullOrWhiteSpace(source)) throw new ArgumentNullException(nameof(source));

            if (Uri.TryCreate(source, UriKind.Absolute, out var uri)) {
                if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                    return new NuGetFeedRepository(source, CredentialProvider?.Invoke(uri));
                if (uri.IsFile)
                    return new LocalPackageRepository(uri.LocalPath);
            }
            return new LocalPackageRepository(source);
        }
    }

    /// <summary>Folder of .nupkg files (subfolders included), like the local developer feed.</summary>
    public class LocalPackageRepository : IPackageRepository
    {
        public LocalPackageRepository(string path) {
            Source = path ?? throw new ArgumentNullException(nameof(path));
        }

        public string Source { get; }

        /// <summary>Every version of every package. An invalid package throws.</summary>
        public IEnumerable<IPackage> GetPackages() {
            if (!Directory.Exists(Source)) return new List<IPackage>();

            var packages = Directory.EnumerateFiles(Source, "*.nupkg", SearchOption.AllDirectories)
                .Select(GamePackage.FromFile)
                .ToList();
            GamePackage.MarkLatestVersions(packages);
            return packages.Cast<IPackage>().ToList();
        }

        public IEnumerable<IPackage> FindPackages(IEnumerable<string> packageIds) {
            var ids = new HashSet<string>(packageIds, StringComparer.OrdinalIgnoreCase);
            return GetPackages().Where(p => ids.Contains(p.Id)).ToList();
        }
    }

    /// <summary>Remote NuGet feed, through NuGet.Protocol.</summary>
    public class NuGetFeedRepository : IPackageRepository
    {
        private const int PageSize = 100;
        private const int MaxPackages = 10000;

        private readonly SourceRepository _repository;

        public NuGetFeedRepository(string source, NetworkCredential credential = null) {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            var packageSource = new PackageSource(source);
            if (credential != null && !string.IsNullOrEmpty(credential.UserName)) {
                packageSource.Credentials = PackageSourceCredential.FromUserInput(
                    source, credential.UserName, credential.Password, storePasswordInClearText: true, validAuthenticationTypesText: null);
            }
            _repository = Repository.Factory.GetCoreV3(packageSource);
        }

        public string Source { get; }

        /// <summary>Latest version (prerelease included) of every package, as the feed's search returns them.</summary>
        public IEnumerable<IPackage> GetPackages() {
            return Run(async ct => {
                var search = await _repository.GetResourceAsync<PackageSearchResource>(ct).ConfigureAwait(false);
                var filter = new SearchFilter(includePrerelease: true);
                var result = new List<IPackage>();
                for (var skip = 0; skip < MaxPackages; skip += PageSize) {
                    var page = (await search.SearchAsync(string.Empty, filter, skip, PageSize, NullLogger.Instance, ct).ConfigureAwait(false)).ToList();
                    foreach (var metadata in page)
                        result.Add(FromMetadata(metadata, isAbsoluteLatest: true, isLatest: !metadata.Identity.Version.IsPrerelease));
                    if (page.Count < PageSize) break;
                }
                return result;
            });
        }

        /// <summary>Latest version (prerelease included) of each requested id that the feed has.</summary>
        public IEnumerable<IPackage> FindPackages(IEnumerable<string> packageIds) {
            var ids = packageIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return Run(async ct => {
                var resource = await _repository.GetResourceAsync<PackageMetadataResource>(ct).ConfigureAwait(false);
                var result = new List<IPackage>();
                using (var cache = NewCache()) {
                    foreach (var id in ids) {
                        var versions = (await resource.GetMetadataAsync(id, true, false, cache, NullLogger.Instance, ct).ConfigureAwait(false)).ToList();
                        if (versions.Count == 0) continue;
                        var latest = versions.OrderByDescending(v => v.Identity.Version, VersionComparer.Default).First();
                        var latestStable = versions.Where(v => !v.Identity.Version.IsPrerelease)
                            .OrderByDescending(v => v.Identity.Version, VersionComparer.Default).FirstOrDefault();
                        result.Add(FromMetadata(latest, isAbsoluteLatest: true, isLatest: ReferenceEquals(latest, latestStable)));
                    }
                }
                return result;
            });
        }

        private GamePackage FromMetadata(IPackageSearchMetadata metadata, bool isAbsoluteLatest, bool isLatest) {
            var identity = metadata.Identity;
            return new GamePackage(() => Download(identity)) {
                Id = identity.Id,
                Version = new SemanticVersion(identity.Version),
                Title = string.IsNullOrWhiteSpace(metadata.Title) ? identity.Id : metadata.Title,
                Description = metadata.Description,
                Summary = metadata.Summary,
                Authors = SplitList(metadata.Authors),
                Tags = metadata.Tags,
                IconUrl = metadata.IconUrl,
                ProjectUrl = metadata.ProjectUrl,
                Published = metadata.Published,
                DownloadCount = metadata.DownloadCount.HasValue ? (int)Math.Min(int.MaxValue, metadata.DownloadCount.Value) : 0,
                IsAbsoluteLatestVersion = isAbsoluteLatest,
                IsLatestVersion = isLatest,
            };
        }

        private string Download(PackageIdentity identity) {
            var directory = Path.Combine(Path.GetTempPath(), "octgn", "packages", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, identity.Id + "." + identity.Version.ToNormalizedString() + ".nupkg");
            var found = Run(async ct => {
                var resource = await _repository.GetResourceAsync<FindPackageByIdResource>(ct).ConfigureAwait(false);
                using (var cache = NewCache())
                using (var stream = File.Create(file)) {
                    return await resource.CopyNupkgToStreamAsync(identity.Id, identity.Version, stream, cache, NullLogger.Instance, ct).ConfigureAwait(false);
                }
            });
            if (!found) throw new WebException($"Package {identity} was not found on {Source}.");
            return file;
        }

        private static SourceCacheContext NewCache() {
            // Always ask the feed: game updates must be seen at once.
            return new SourceCacheContext { NoCache = true, DirectDownload = true };
        }

        private static IEnumerable<string> SplitList(string value) {
            if (string.IsNullOrWhiteSpace(value)) return new string[0];
            return value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
        }

        // GameFeedManager is synchronous and is called from the UI thread too: run outside of any
        // synchronization context, and turn transport errors into WebException as NuGet.Core did.
        private T Run<T>(Func<CancellationToken, Task<T>> operation) {
            try {
                return Task.Run(() => operation(CancellationToken.None)).GetAwaiter().GetResult();
            } catch (Exception ex) when (IsUnauthorized(ex)) {
                throw new FeedAuthenticationRequiredException(Source, ex);
            } catch (Exception ex) when (ex is FatalProtocolException || ex is HttpRequestException || ex is IOException || ex is TaskCanceledException) {
                throw new WebException($"Error reading the feed {Source}: {ex.Message}", ex);
            }
        }

        private static bool IsUnauthorized(Exception ex) {
            for (var e = ex; e != null; e = e.InnerException) {
                if (e is HttpRequestException http && http.StatusCode == HttpStatusCode.Unauthorized) return true;
                if (e.Message != null && e.Message.Contains("401 (Unauthorized)")) return true;
            }
            return false;
        }
    }

    /// <summary>Package read from a .nupkg file, local or downloaded on first use.</summary>
    internal sealed class GamePackage : IPackage
    {
        private readonly Lazy<string> _nupkgPath;

        public GamePackage(Func<string> getNupkgPath) {
            _nupkgPath = new Lazy<string>(getNupkgPath, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public string Id { get; set; }
        public SemanticVersion Version { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Summary { get; set; }
        public IEnumerable<string> Authors { get; set; }
        public string Tags { get; set; }
        public Uri IconUrl { get; set; }
        public Uri ProjectUrl { get; set; }
        public DateTimeOffset? Published { get; set; }
        public int DownloadCount { get; set; }
        public bool IsLatestVersion { get; set; }
        public bool IsAbsoluteLatestVersion { get; set; }

        public override string ToString() => Id + " " + Version;

        public static GamePackage FromFile(string path) {
            using (var reader = new PackageArchiveReader(path)) {
                var nuspec = reader.NuspecReader;
                var id = nuspec.GetId();
                return new GamePackage(() => path) {
                    Id = id,
                    Version = new SemanticVersion(nuspec.GetVersion()),
                    Title = string.IsNullOrWhiteSpace(nuspec.GetTitle()) ? id : nuspec.GetTitle(),
                    Description = nuspec.GetDescription(),
                    Summary = nuspec.GetSummary(),
                    Authors = (nuspec.GetAuthors() ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray(),
                    Tags = nuspec.GetTags(),
                    IconUrl = ToUri(nuspec.GetIconUrl()),
                    ProjectUrl = ToUri(nuspec.GetProjectUrl()),
                    Published = File.GetLastWriteTimeUtc(path),
                };
            }
        }

        /// <summary>Flags the latest version of each id (prerelease included or not), as NuGet.Core did.</summary>
        public static void MarkLatestVersions(IEnumerable<GamePackage> packages) {
            foreach (var group in packages.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)) {
                var ordered = group.OrderByDescending(p => p.Version).ToList();
                ordered[0].IsAbsoluteLatestVersion = true;
                var latestStable = ordered.FirstOrDefault(p => !p.Version.IsPrerelease);
                if (latestStable != null) latestStable.IsLatestVersion = true;
            }
        }

        public IEnumerable<IPackageFile> GetFiles() {
            var path = _nupkgPath.Value;
            using (var reader = new PackageArchiveReader(path)) {
                return reader.GetFiles()
                    .Where(f => !IsNuGetMetadata(f))
                    .Select(f => (IPackageFile)new ArchiveFile(path, f))
                    .ToList();
            }
        }

        private static bool IsNuGetMetadata(string entry) {
            if (entry == "[Content_Types].xml" || entry == ".signature.p7s") return true;
            if (entry.StartsWith("_rels/", StringComparison.Ordinal) || entry.StartsWith("package/", StringComparison.Ordinal)) return true;
            return entry.IndexOf('/') < 0 && entry.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase);
        }

        private static Uri ToUri(string value) {
            return Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
        }

        private sealed class ArchiveFile : IPackageFile
        {
            private readonly string _nupkg;
            private readonly string _entry;

            public ArchiveFile(string nupkg, string entry) {
                _nupkg = nupkg;
                _entry = entry;
                Path = entry.Replace('/', System.IO.Path.DirectorySeparatorChar);
            }

            public string Path { get; }

            public Stream GetStream() {
                // Copy the entry so the archive is not held open by the caller.
                using (var reader = new PackageArchiveReader(_nupkg))
                using (var stream = reader.GetStream(_entry)) {
                    var copy = new MemoryStream();
                    stream.CopyTo(copy);
                    copy.Position = 0;
                    return copy;
                }
            }

            public override string ToString() => Path;
        }
    }
}
