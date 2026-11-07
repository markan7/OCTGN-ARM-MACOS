using NUnit.Framework;
using Octgn.Communication;
using Octgn.Library.Networking;
using Octgn.Online.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Octgn.Test.Library.Networking
{
    [TestFixture]
    public class GameBroadcastingTests
    {
        [TestCase]
        public async Task Test() {
#if NETCOREAPP
            if (OperatingSystem.IsMacOS() && !CanSendMulticast(3456))
                Assert.Ignore("macOS denies this process the local network (Local Network privacy): multicast sends fail with 'No route to host'.");
#endif
            var game = new HostedGame() {
                Id = Guid.NewGuid(),
                HostUser = new User("id", "name"),
                // The listener reads Host, which requires an address.
                HostAddress = "0.0.0.0:9999"
            };
            using(var broadcaster = new GameBroadcaster(game, 3456)) {
                using(var listener = new GameBroadcastListener(3456)) {
                    broadcaster.StartBroadcasting();
                    listener.StartListening();

                    var waitedGame = await listener.WaitForGame(game.Id);

                    Assert.AreEqual(game.HostUser.Id, waitedGame.HostUser.Id);
                    Assert.AreEqual(game.HostUser.DisplayName, waitedGame.HostUser.DisplayName);
                }
            }
        }

#if NETCOREAPP
        private static bool CanSendMulticast(int port) {
            using (var probe = new System.Net.Sockets.UdpClient()) {
                try {
                    probe.Send(new byte[] { 0 }, 1, new System.Net.IPEndPoint(GameBroadcaster.MulticastAddress, port));
                    return true;
                } catch (System.Net.Sockets.SocketException e) when (e.SocketErrorCode == System.Net.Sockets.SocketError.HostUnreachable) {
                    return false;
                }
            }
        }
#endif
    }
}
