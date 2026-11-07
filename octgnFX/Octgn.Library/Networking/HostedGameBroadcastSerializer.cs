// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.IO;
using Octgn.Online.Hosting;

#if NETFRAMEWORK
using System.Runtime.Serialization.Formatters.Binary;
#endif

namespace Octgn.Library.Networking
{
    /// <summary>
    /// Payload of the LAN game broadcasts (<see cref="GameBroadcaster"/>, <see cref="GameBroadcastListener"/>):
    /// a <see cref="HostedGame"/> in the .NET binary serialization format (MS-NRBF), the format
    /// <c>BinaryFormatter</c> produces on the Windows client.
    /// </summary>
    /// <remarks>
    /// .NET 10 no longer ships <c>BinaryFormatter</c>. The cross-platform build writes the same bytes with
    /// <see cref="NrbfHostedGameWriter"/> and reads them with System.Formats.Nrbf, which decodes the records
    /// without instantiating the types named in the payload. Windows clients and the cross-platform
    /// clients therefore still see each other's LAN games.
    /// </remarks>
    public static class HostedGameBroadcastSerializer
    {
        public static byte[] Serialize(HostedGame game) {
            if (game == null) throw new ArgumentNullException(nameof(game));
#if NETFRAMEWORK
            using (var ms = new MemoryStream()) {
                var ser = new BinaryFormatter();
                ser.Serialize(ms, game);
                return ms.ToArray();
            }
#else
            return NrbfHostedGameWriter.Write(game);
#endif
        }

        public static HostedGame Deserialize(byte[] data) {
            if (data == null) throw new ArgumentNullException(nameof(data));
#if NETFRAMEWORK
            using (var ms = new MemoryStream(data)) {
                ms.Position = 0;
                var bf = new BinaryFormatter();
                return (HostedGame)bf.Deserialize(ms);
            }
#else
            return NrbfHostedGameReader.Read(data);
#endif
        }
    }
}
