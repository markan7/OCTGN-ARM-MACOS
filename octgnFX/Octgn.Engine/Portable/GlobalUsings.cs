// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

// In the files shared with the WPF engine, the WPF namespaces are imported under '#if !NETCOREAPP'.
// In this build, the same type names resolve to their portable stand-ins.
global using Octgn.Portable;
global using Color = Octgn.Core.Media.Color;
global using Colors = Octgn.Core.Media.Colors;
