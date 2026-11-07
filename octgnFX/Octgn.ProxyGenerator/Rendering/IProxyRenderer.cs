// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using Octgn.ProxyGenerator.Definitions;

namespace Octgn.ProxyGenerator.Rendering
{
    /// <summary>
    /// Draws a proxy card image and saves it as a PNG file.
    /// The Windows client renders with GDI+ (<c>ProxyGenerator.GenerateProxy</c>); the cross-platform
    /// build has no built-in renderer and uses the one registered in <see cref="ProxyRendering.Renderer"/>.
    /// </summary>
    public interface IProxyRenderer
    {
        /// <param name="blocks">Blocks of the proxy definition.</param>
        /// <param name="template">Template selected for the card.</param>
        /// <param name="values">Card property values.</param>
        /// <param name="specialPath">Optional art overlay image, or null.</param>
        /// <param name="pngPath">File to write.</param>
        void RenderToPng(BlockManager blocks, TemplateDefinition template, Dictionary<string, string> values, string specialPath, string pngPath);
    }

    public static class ProxyRendering
    {
        /// <summary>
        /// Renderer used by <see cref="ProxyDefinition.SaveProxyImage(Dictionary{string, string}, string, string)"/>
        /// on platforms without GDI+. The application sets it at startup.
        /// </summary>
        public static IProxyRenderer Renderer { get; set; }

        internal static IProxyRenderer GetRenderer() {
            var renderer = Renderer;
            if (renderer == null)
                throw new PlatformNotSupportedException("No proxy image renderer is registered for this platform. Set ProxyRendering.Renderer at startup.");
            return renderer;
        }
    }
}
