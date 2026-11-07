// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NuGet.Versioning;

namespace Octgn.Core.Packaging
{
    // Cross-platform build only. The Windows client uses NuGet.Core 2.x (.NET Framework only); these types
    // cover the part of its API that GameFeedManager and GameManager use, on top of NuGet.Protocol, so those
    // two classes compile unchanged on both targets.

    /// <summary>A game package of a feed (counterpart of NuGet.Core's IPackage).</summary>
    public interface IPackage
    {
        string Id { get; }
        SemanticVersion Version { get; }
        string Title { get; }
        string Description { get; }
        string Summary { get; }
        IEnumerable<string> Authors { get; }
        string Tags { get; }
        Uri IconUrl { get; }
        Uri ProjectUrl { get; }
        DateTimeOffset? Published { get; }
        int DownloadCount { get; }
        /// <summary>Latest stable version of this package id in the feed.</summary>
        bool IsLatestVersion { get; }
        /// <summary>Latest version of this package id in the feed, prerelease included.</summary>
        bool IsAbsoluteLatestVersion { get; }
        /// <summary>Files of the package, without the NuGet metadata (nuspec, _rels, package/...).</summary>
        IEnumerable<IPackageFile> GetFiles();
    }

    /// <summary>A file inside a package.</summary>
    public interface IPackageFile
    {
        /// <summary>Relative path, with the platform's directory separator.</summary>
        string Path { get; }
        Stream GetStream();
    }

    /// <summary>Package version (counterpart of NuGet.Core's SemanticVersion), backed by NuGet.Versioning.</summary>
    public sealed class SemanticVersion : IComparable<SemanticVersion>, IComparable, IEquatable<SemanticVersion>
    {
        private readonly NuGetVersion _version;

        public SemanticVersion(string version) : this(NuGetVersion.Parse(version)) {
        }

        public SemanticVersion(Version version) : this(new NuGetVersion(version ?? throw new ArgumentNullException(nameof(version)))) {
        }

        internal SemanticVersion(NuGetVersion version) {
            _version = version ?? throw new ArgumentNullException(nameof(version));
        }

        /// <summary>Numeric part of the version.</summary>
        public Version Version => _version.Version;

        /// <summary>Prerelease label, or an empty string.</summary>
        public string SpecialVersion => _version.Release;

        public bool IsPrerelease => _version.IsPrerelease;

        internal NuGetVersion NuGetVersion => _version;

        public int CompareTo(SemanticVersion other) {
            if (ReferenceEquals(other, null)) return 1;
            return VersionComparer.Default.Compare(_version, other._version);
        }

        public int CompareTo(object obj) {
            if (obj == null) return 1;
            if (obj is SemanticVersion other) return CompareTo(other);
            throw new ArgumentException($"Object must be a {nameof(SemanticVersion)}.", nameof(obj));
        }

        public bool Equals(SemanticVersion other) => CompareTo(other) == 0;

        public override bool Equals(object obj) => obj is SemanticVersion other && Equals(other);

        public override int GetHashCode() => VersionComparer.Default.GetHashCode(_version);

        public override string ToString() => _version.ToString();

        public static bool operator ==(SemanticVersion left, SemanticVersion right) {
            return ReferenceEquals(left, null) ? ReferenceEquals(right, null) : left.Equals(right);
        }

        public static bool operator !=(SemanticVersion left, SemanticVersion right) => !(left == right);

        public static bool operator <(SemanticVersion left, SemanticVersion right) => Compare(left, right) < 0;

        public static bool operator >(SemanticVersion left, SemanticVersion right) => Compare(left, right) > 0;

        public static bool operator <=(SemanticVersion left, SemanticVersion right) => Compare(left, right) <= 0;

        public static bool operator >=(SemanticVersion left, SemanticVersion right) => Compare(left, right) >= 0;

        private static int Compare(SemanticVersion left, SemanticVersion right) {
            if (ReferenceEquals(left, null)) return ReferenceEquals(right, null) ? 0 : -1;
            return left.CompareTo(right);
        }
    }

    /// <summary>Feed address checks (counterpart of NuGet.Core's PathValidator).</summary>
    public static class PathValidator
    {
        /// <summary>A local path, a UNC path or a URL.</summary>
        public static bool IsValidSource(string source) {
            if (string.IsNullOrWhiteSpace(source)) return false;
            return IsValidLocalPath(source) || IsValidUncPath(source) || IsValidUrl(source);
        }

        public static bool IsValidLocalPath(string path) {
            try {
                return Path.IsPathRooted(path) && path.IndexOfAny(Path.GetInvalidPathChars()) == -1;
            } catch (ArgumentException) {
                return false;
            }
        }

        public static bool IsValidUncPath(string path) {
            return path.StartsWith(@"\\", StringComparison.Ordinal);
        }

        /// <summary>An absolute URL with a scheme ("scheme://..."); Uri alone would accept local paths too.</summary>
        public static bool IsValidUrl(string url) {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return Regex.IsMatch(url, @"^\w+://", RegexOptions.IgnoreCase) && Uri.TryCreate(url, UriKind.Absolute, out _);
        }
    }

    /// <summary>The feed answered 401: it requires credentials, or the given ones were refused.</summary>
    public class FeedAuthenticationRequiredException : System.Net.WebException
    {
        public FeedAuthenticationRequiredException(string source, Exception innerException)
            : base($"The feed {source} requires authentication.", innerException, System.Net.WebExceptionStatus.ProtocolError, null) {
        }
    }
}
