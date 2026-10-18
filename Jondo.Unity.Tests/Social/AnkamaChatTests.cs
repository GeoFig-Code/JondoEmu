using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Xunit;

namespace Jondo.Unity.Tests.Social
{
    /// <summary>
    /// The Ankama chat service between two clients over a real socket (TLS aside): Spin frames,
    /// the launcher's token, the ping, and an invitation that ends in a friendship, seen online and
    /// then offline, and undone.
    /// </summary>
    [Collection("ClientLaunchRegistry")]
    public sealed class AnkamaChatTests
    {
        private const long AccountA = 8_880_000_001, AccountB = 8_880_000_002;

        private sealed class Client : IAsyncDisposable
        {
            private readonly TcpListener _listener;
            private readonly TcpClient _client, _server;
            private int _correlation;
            public NetworkStream Stream { get; }
            public Task Serving { get; }

            private Client(TcpListener listener, TcpClient client, TcpClient server)
            {
                _listener = listener;
                _client = client;
                _server = server;
                Stream = client.GetStream();
                Serving = ChatServer.ServeAsync(server.GetStream());
            }

            public static async Task<Client> ConnectAsync()
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
                var server = await listener.AcceptTcpClientAsync();
                return new Client(listener, client, server);
            }

            public async Task SendAsync(byte type, byte[] payload)
            {
                int length = 5 + payload.Length;
                var frame = new byte[length];
                frame[0] = (byte)(length >> 24); frame[1] = (byte)(length >> 16);
                frame[2] = (byte)(length >> 8); frame[3] = (byte)length;
                frame[4] = type;
                payload.CopyTo(frame, 5);
                await Stream.WriteAsync(frame);
            }

            public async Task<(byte Type, byte[] Payload)> ReadAsync()
            {
                var header = await ExactAsync(4);
                int length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
                var body = await ExactAsync(length - 4);
                return (body[0], body.Skip(1).ToArray());
            }

            private async Task<byte[]> ExactAsync(int count)
            {
                var buffer = new byte[count];
                int read = 0;
                var deadline = Task.Delay(TimeSpan.FromSeconds(10));
                while (read < count)
                {
                    var next = Stream.ReadAsync(buffer, read, count - read);
                    Assert.True(await Task.WhenAny(next, deadline) == next, "nothing arrived");
                    int n = await next;
                    Assert.True(n > 0, "the service closed the connection");
                    read += n;
                }
                return buffer;
            }

            public async Task<string> LoginAsync(string token)
            {
                await SendAsync(ChatServer.Application, Encoding.UTF8.GetBytes($"{{\"token\":\"{token}\",\"clientType\":2}}"));
                var (type, payload) = await ReadAsync();
                Assert.Equal(ChatServer.Application, type);
                return Encoding.UTF8.GetString(payload);
            }

            /// <summary>Sends a request and returns its correlation id.</summary>
            public async Task<int> RequestAsync(uint id, Pb data)
            {
                int correlation = ++_correlation;
                var frame = Pb.New().Msg(1, Pb.New().Var(1, correlation)
                    .Msg(2, Pb.New().Var(1, (int)id).Bytes(2, data.Build())));
                await SendAsync(ChatServer.Application, frame.Build());
                return correlation;
            }

            /// <summary>The next application frame: (response?, its status, payload id, payload data).</summary>
            public async Task<(bool Response, int Correlation, long Status, uint Id, ProtoMessage Data)> NextAsync()
            {
                var (type, payload) = await ReadAsync();
                Assert.Equal(ChatServer.Application, type);
                var frame = ProtoMessage.Parse(payload);
                var response = frame.Fields.FirstOrDefault(f => f.FieldNumber == 2);
                if (response != null)
                {
                    var r = ProtoMessage.Parse(response.BytesValue);
                    var p = r.Fields.FirstOrDefault(f => f.FieldNumber == 3);
                    var (id, data) = p == null ? (0u, new ProtoMessage()) : Payload(p.BytesValue);
                    return (true, (int)Var(r, 1), Var(r, 2), id, data);
                }
                var evt = frame.Fields.Single(f => f.FieldNumber == 3);
                var (eid, edata) = Payload(evt.BytesValue);
                return (false, 0, 0, eid, edata);
            }

            private static (uint, ProtoMessage) Payload(byte[] bytes)
            {
                var p = ProtoMessage.Parse(bytes);
                var data = p.Fields.FirstOrDefault(f => f.FieldNumber == 2)?.BytesValue ?? Array.Empty<byte>();
                return ((uint)Var(p, 1), ProtoMessage.Parse(data));
            }

            public async ValueTask DisposeAsync()
            {
                _client.Dispose();
                try { await Serving.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
                _server.Dispose();
                _listener.Stop();
            }
        }

        private static long Var(ProtoMessage m, int field)
            => m.Fields.FirstOrDefault(f => f.FieldNumber == field && f.WireType == 0)?.VarIntValue ?? 0;

        private static ProtoMessage Sub(ProtoMessage m, int field)
            => ProtoMessage.Parse(m.Fields.First(f => f.FieldNumber == field).BytesValue);

        [Fact]
        public async Task A_token_nobody_was_given_is_refused()
        {
            await using var stranger = await Client.ConnectAsync();
            string answer = await stranger.LoginAsync("nadie-me-dio-este-token");
            Assert.Contains("\"success\":false", answer);
            Assert.Contains("\"errCode\":1", answer);
        }

        [Fact]
        public async Task An_invitation_accepted_makes_two_friends_who_see_each_other()
        {
            AnkamaFriends.Unfriend(AccountA, AccountB);
            AnkamaFriends.Withdraw(AccountA, AccountB);
            AnkamaFriends.Withdraw(AccountB, AccountA);
            ClientLaunchRegistry.RegisterToken(AccountA, "chat-token-a");
            ClientLaunchRegistry.RegisterToken(AccountB, "chat-token-b");

            await using var a = await Client.ConnectAsync();
            Assert.Equal("{\"success\":true}", await a.LoginAsync("chat-token-a"));

            // The ping comes back as it went.
            var ping = Enumerable.Range(0, 56).Select(i => (byte)i).ToArray();
            await a.SendAsync(ChatServer.Ping, ping);
            var (type, pong) = await a.ReadAsync();
            Assert.Equal(ChatServer.Pong, type);
            Assert.Equal(ping, pong);

            int asked = await a.RequestAsync(ChatServer.Ids.ListUserFriendsCmd, Pb.New().Var(1, AccountA));
            var list = await a.NextAsync();
            Assert.True(list.Response);
            Assert.Equal(asked, list.Correlation);
            Assert.Equal(ChatServer.Success, list.Status);
            Assert.Equal(ChatServer.Ids.FriendList, list.Id);
            Assert.Empty(list.Data.Fields);

            await using var b = await Client.ConnectAsync();
            Assert.Equal("{\"success\":true}", await b.LoginAsync("chat-token-b"));

            // Nobody by that nickname: a bad request.
            await a.RequestAsync(ChatServer.Ids.CreateFriendInviteCmd,
                Pb.New().Var(1, AccountA).Msg(4, Pb.New().Str(1, "Nadie-Se-Llama-Asi").Str(2, "0000")));
            Assert.Equal(ChatServer.BadRequest, (await a.NextAsync()).Status);

            await a.RequestAsync(ChatServer.Ids.CreateFriendInviteCmd, Pb.New().Var(1, AccountA).Var(2, AccountB));
            var sent = await a.NextAsync();
            Assert.Equal(ChatServer.Success, sent.Status);
            Assert.Equal(ChatServer.Ids.FriendInvite, sent.Id);
            Assert.Equal(ChatServer.Ids.FriendInviteCreatedEvt, (await a.NextAsync()).Id);
            var received = await b.NextAsync();
            Assert.Equal(ChatServer.Ids.FriendInviteCreatedEvt, received.Id);
            Assert.Equal(AccountA, Var(Sub(Sub(received.Data, 1), 1), 1));

            await b.RequestAsync(ChatServer.Ids.ListUserFriendInvitesCmd, Pb.New().Var(1, AccountB));
            var invites = await b.NextAsync();
            Assert.Equal(ChatServer.Ids.FriendInviteList, invites.Id);
            Assert.Single(invites.Data.Fields);

            // B accepts.
            await b.RequestAsync(ChatServer.Ids.DeleteFriendInviteCmd,
                Pb.New().Var(1, AccountA).Var(2, AccountB).Var(3, ChatServer.Accepted));
            Assert.Equal(ChatServer.Success, (await b.NextAsync()).Status);
            foreach (var side in new[] { a, b })
            {
                Assert.Equal(ChatServer.Ids.FriendInviteDeletedEvt, (await side.NextAsync()).Id);
                Assert.Equal(ChatServer.Ids.UserFriendCreatedEvt, (await side.NextAsync()).Id);
                var presence = await side.NextAsync();
                Assert.Equal(ChatServer.Ids.UserPresenceUpdatedEvt, presence.Id);
                Assert.Equal(ChatServer.Online, Var(presence.Data, 2));
            }
            Assert.True(AnkamaFriends.AreFriends(AccountA, AccountB));

            // A's list has B, and B is seen connected.
            await a.RequestAsync(ChatServer.Ids.ListUserFriendsCmd, Pb.New().Var(1, AccountA));
            var friends = await a.NextAsync();
            Assert.Equal(AccountB, Var(Sub(Sub(friends.Data, 1), 1), 1));
            var online = await a.NextAsync();
            Assert.Equal(ChatServer.Ids.UserPresenceUpdatedEvt, online.Id);
            Assert.Equal(AccountB, Var(online.Data, 1));
            Assert.Equal(ChatServer.Ids.UserEndpointPropertiesUpdatedEvt, (await a.NextAsync()).Id);

            // A leaves: B sees him offline.
            await a.DisposeAsync();
            var gone = await b.NextAsync();
            Assert.Equal(ChatServer.Ids.UserPresenceUpdatedEvt, gone.Id);
            Assert.Equal(ChatServer.Offline, Var(gone.Data, 2));

            await b.RequestAsync(ChatServer.Ids.DeleteUserFriendCmd, Pb.New().Var(1, AccountB).Var(2, AccountA));
            Assert.Equal(ChatServer.Success, (await b.NextAsync()).Status);
            Assert.Equal(ChatServer.Ids.UserFriendDeletedEvt, (await b.NextAsync()).Id);
            Assert.False(AnkamaFriends.AreFriends(AccountA, AccountB));
        }
    }
}
