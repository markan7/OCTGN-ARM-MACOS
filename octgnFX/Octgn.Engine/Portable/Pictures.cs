// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

// Card pictures in this build: the engine says which picture to show, the user interface loads it
// (the WPF engine loads BitmapImage objects itself: Card.GetBitmapImage, Player.SleeveImage,
// GameEngine.GetCardFront and GetCardBack).

using System;
using log4net;
using Octgn.Core.DataExtensionMethods;
using Octgn.DataNew.Entities;

namespace Octgn.Play
{
    public sealed partial class Card
    {
        /// <summary>
        /// The picture to show for this card, as Card.GetBitmapImage chooses it in WPF: face up, the card's
        /// picture (the proxy picture with <paramref name="proxyOnly"/>), or the front of its size when it has
        /// none; face down, the back of its size. A file path, or for the default pictures of the client a
        /// "pack://application:,,,/Resources/..." URI. When the owner has a deck sleeve
        /// (<see cref="Player.SleeveImage"/>), it replaces the back: the user interface shows it then.
        /// </summary>
        internal string GetPictureUri(bool up, bool proxyOnly = false) {
            if (!up) return Size.Back;
            if (Type == null || Type.Model == null) return Size.Front;
            try {
                return proxyOnly ? Type.Model.GetProxyPicture() : Type.Model.GetPicture();
            } catch (Exception ex) {
                // No picture and no proxy (no proxy renderer, or a broken game): the front of the size.
                LogManager.GetLogger(typeof(Card)).Warn($"No picture for card {Type.Model.Name} ({Type.Model.Id})", ex);
                return Size.Front;
            }
        }
    }

    public sealed partial class Player
    {
        private byte[] _sleeveImage;

        /// <summary>The encoded image (PNG, JPEG) of the backs of this player's cards, or null for the backs of the game.</summary>
        public byte[] SleeveImage {
            get => _sleeveImage;
            set {
                if (value != _sleeveImage) {
                    _sleeveImage = value;
                    OnPropertyChanged(nameof(SleeveImage));
                }
            }
        }

        public void SetSleeve(ISleeve sleeve) {
            SleeveImage = sleeve?.ImageData;
        }
    }
}
