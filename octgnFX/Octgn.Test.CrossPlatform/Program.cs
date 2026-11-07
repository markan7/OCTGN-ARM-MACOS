// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using NUnitLite;

namespace Octgn.Test
{
    public static class Program
    {
        // NUnitLite options apply, for example: --where "cat != Network" or --test=Octgn.Test.Core
        public static int Main(string[] args) {
            return new AutoRun(typeof(Program).Assembly).Execute(args);
        }
    }
}
