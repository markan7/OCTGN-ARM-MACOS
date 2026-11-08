// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System.Runtime.CompilerServices;

// Much of the model is internal, as it was inside the WPF engine's own assembly: the user interface
// and the tests of the cross-platform build use it as the WPF views do.
[assembly: InternalsVisibleTo("Octgn.Desktop")]
[assembly: InternalsVisibleTo("Octgn.Test.CrossPlatform")]
[assembly: InternalsVisibleTo("Octgn.Desktop.Test")]
