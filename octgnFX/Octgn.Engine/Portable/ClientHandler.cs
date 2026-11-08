// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using System.Linq;
using Octgn.DataNew.Entities;
using Octgn.Core.DataExtensionMethods;

namespace Octgn.Networking
{
    public sealed partial class Handler
    {
        /// <summary>"[Set - Pack],[Set - Pack]" for the chat, as PickCardsDialog.PackNames in WPF.</summary>
        private static string PackNames(IEnumerable<Guid> packs) {
            var names = new List<string>();
            foreach (var pack in packs.Select(Program.GameEngine.Definition.GetPackById)) {
                if (pack == null) {
                    Program.GameMess.Warning("Received pack is missing from the database. Pack is ignored.");
                    continue;
                }
                names.Add(string.Format("[{0} - {1}]", pack.Set.Name, pack.Name));
            }
            return string.Join(",", names);
        }
    }
}
