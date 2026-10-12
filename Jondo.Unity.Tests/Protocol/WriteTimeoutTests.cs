using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Jondo.Protocol;
using Xunit;

namespace Jondo.Unity.Tests.Protocol
{
    /// <summary>
    /// A client that does not read cannot hold whoever writes to it: the write gives up after
    /// its time and the socket is closed, instead of waiting for ever with a fight held behind it.
    /// </summary>
    public class WriteTimeoutTests
    {
        [Fact]
        public async Task A_client_that_does_not_read_is_given_up()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
                using var server = await listener.AcceptTcpClientAsync();
                var stream = server.GetStream();

                // Megabyte frames until the buffers are full and a write has to wait: it gives up
                // after a second, and from then on every write is dropped at once.
                var frame = new byte[1 << 20];
                var clock = Stopwatch.StartNew();
                for (int i = 0; i < 256 && clock.Elapsed < TimeSpan.FromSeconds(30); i++)
                    await NetworkMessage.WriteSerializedAsync(stream, frame, TimeSpan.FromSeconds(1));

                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"took {clock.Elapsed}");
                Assert.Throws<ObjectDisposedException>(() => stream.Write(new byte[1], 0, 1));
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
