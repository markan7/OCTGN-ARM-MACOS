// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using log4net;
using Octgn.Play;
using Octgn.Portable;

namespace Octgn.Desktop
{
    /// <summary>
    /// The pictures of the game (cards, sleeves, table, markers, pile icons), decoded once and kept: the
    /// engine names them (file paths, "pack://application:,,,/" URIs of the client's defaults), the user
    /// interface loads them. A picture that cannot be read is replaced by the default card front.
    /// </summary>
    public static class Pictures
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(Pictures));
        private static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>(StringComparer.Ordinal);
        private static readonly ConditionalWeakTable<byte[], Bitmap> Sleeves = new ConditionalWeakTable<byte[], Bitmap>();

        /// <summary>Card pictures are decoded at most this wide: enough for the preview of the hovered card.</summary>
        public const int MaxCardWidth = 480;

        private const string DefaultFront = PackUri.ApplicationPrefix + "Resources/Front.jpg";

        /// <summary>The picture at <paramref name="source"/>, or null if neither it nor the default front can be read.</summary>
        public static Bitmap Get(string source, int maxWidth = MaxCardWidth) {
            if (string.IsNullOrEmpty(source)) return null;
            var key = source + "|" + maxWidth;
            lock (Cache) {
                if (Cache.TryGetValue(key, out var cached)) return cached;
            }
            var bitmap = Load(source, maxWidth);
            if (bitmap == null && source != DefaultFront) bitmap = Get(DefaultFront, maxWidth);
            lock (Cache) Cache[key] = bitmap;
            return bitmap;
        }

        private static readonly Dictionary<(Guid, string, bool), string> CardPictures = new Dictionary<(Guid, string, bool), string>();

        /// <summary>The picture to show for a card, face up or down, as the WPF client chooses it.</summary>
        public static Bitmap ForCard(Card card, bool up, bool proxyOnly = false) => ForCard(card, up, proxyOnly, out _);

        /// <summary>
        /// The picture of a card; <paramref name="generic"/>: face up, the card has no picture of its own (nor
        /// a proxy, see Octgn.ProxyGenerator), and the front of its size stands for it.
        /// </summary>
        public static Bitmap ForCard(Card card, bool up, bool proxyOnly, out bool generic) {
            generic = false;
            if (!up && card.Owner?.SleeveImage is byte[] sleeve) {
                var image = Sleeve(sleeve);
                if (image != null) return image;
            }
            var model = card.Type?.Model;
            if (!up || model == null) return Get(card.GetPictureUri(up, proxyOnly));
            // Finding a card's picture reads its set's folders (Core's GetPicture): once per card and side.
            var key = (model.Id, model.Alternate ?? "", proxyOnly);
            string uri;
            lock (CardPictures) {
                if (!CardPictures.TryGetValue(key, out uri)) CardPictures[key] = uri = card.GetPictureUri(true, proxyOnly);
            }
            generic = uri == card.Size.Front;
            return Get(uri);
        }

        private static Bitmap Sleeve(byte[] data) {
            lock (Sleeves) {
                if (Sleeves.TryGetValue(data, out var bitmap)) return bitmap;
                try {
                    using (var stream = new MemoryStream(data)) bitmap = Bitmap.DecodeToWidth(stream, MaxCardWidth);
                } catch (Exception ex) {
                    Log.Warn("Unreadable deck sleeve", ex);
                    bitmap = null;
                }
                Sleeves.AddOrUpdate(data, bitmap);
                return bitmap;
            }
        }

        private static Bitmap Load(string source, int maxWidth) {
            try {
                var path = PackUri.ToFilePath(source);
                if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile) path = uri.LocalPath;
                if (!File.Exists(path)) {
                    Log.Warn("Missing picture " + source);
                    return null;
                }
                using (var stream = File.OpenRead(path)) {
                    var bitmap = new Bitmap(stream);
                    if (bitmap.PixelSize.Width <= maxWidth) return bitmap;
                    var height = Math.Max(1, (int)Math.Round(bitmap.PixelSize.Height * (double)maxWidth / bitmap.PixelSize.Width));
                    var scaled = bitmap.CreateScaledBitmap(new Avalonia.PixelSize(maxWidth, height), BitmapInterpolationMode.HighQuality);
                    bitmap.Dispose();
                    return scaled;
                }
            } catch (Exception ex) {
                Log.Warn("Unreadable picture " + source, ex);
                return null;
            }
        }
    }
}
