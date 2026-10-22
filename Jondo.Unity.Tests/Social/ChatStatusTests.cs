using System;
using System.Linq;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.Tests.Economy;
using Xunit;

namespace Jondo.Unity.Tests.Social
{
    /// <summary>
    /// The chat status: what kus reads, what ktx says and to whom, and what «private» and «solo»
    /// keep away.
    /// </summary>
    [Collection("friends")]
    public sealed class ChatStatusTests
    {
        private const long AccountA = 8_871_000_001, AccountB = 8_871_000_002, AccountC = 8_871_000_003;
        private const long CharacterA = 8_871_100_001, CharacterB = 8_871_100_002, CharacterC = 8_871_100_003;
        private const long Map = 8_871_555_001;

        private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

        private static void Forget()
        {
            long[] all = { AccountA, AccountB, AccountC };
            foreach (long a in all)
            {
                Contacts.ForgetSession(a);
                foreach (long b in all) Contacts.Remove(a, b, Contacts.Contact);
            }
        }

        /// <summary>kus as eak::usm builds it: f2, an lcf with the status alone.</summary>
        private static byte[] Kus(int status)
            => ConnectionProtocol.Push(Op.Kus, Pb.New().Msg(2, Pb.New().Var(1, status)).Build());

        /// <summary>kus as the commands with a message build it: f1, status and text.</summary>
        private static byte[] Kus(int status, string message)
            => ConnectionProtocol.Push(Op.Kus, Pb.New().Msg(1, Pb.New().Var(1, status).Str(2, message)).Build());

        [Fact]
        public void Kus_carries_the_status_in_f2_or_with_its_message_in_f1()
        {
            Assert.Equal((ChatStatusHandler.Idle, ""),
                ChatStatusHandler.Read(Pb.New().Msg(2, Pb.New().Var(1, 3)).Build()));
            Assert.Equal((ChatStatusHandler.Away, "brb"),
                ChatStatusHandler.Read(Pb.New().Msg(1, Pb.New().Var(1, 2).Str(2, "  brb ")).Build()));
        }

        [Fact]
        public void Ktx_is_the_lcf_and_the_character()
        {
            Assert.Equal("0a0708021203627262102a", Hex(ChatStatusHandler.BuildUpdate(42, ChatStatusHandler.Away, "brb")));
            Assert.Equal("0a020804102a", Hex(ChatStatusHandler.BuildUpdate(42, ChatStatusHandler.Private, "")));
            Assert.Empty(ChatStatusHandler.Lcf(ChatStatusHandler.Offline).Build());
        }

        [Fact]
        public async Task The_change_goes_to_the_player_and_to_who_keeps_him_as_a_friend()
        {
            Forget();
            await using var a = await ClientPipe.OpenAsync(AccountA, CharacterA, Map, "Estado-Uno");
            await using var b = await ClientPipe.OpenAsync(AccountB, CharacterB, Map, "Estado-Dos");
            await using var c = await ClientPipe.OpenAsync(AccountC, CharacterC, Map, "Estado-Tres");
            Contacts.Add(AccountB, AccountA, Contacts.Contact);

            using (SessionContext.Push(a.Session))
                await ChatStatusHandler.SetAsync(a.ToClient, Kus(ChatStatusHandler.Away, "brb"));

            byte[] expected = ChatStatusHandler.BuildUpdate(CharacterA, ChatStatusHandler.Away, "brb");
            Assert.Equal(expected, await a.Next(Op.Ktx));
            Assert.Equal(expected, await b.Next(Op.Ktx));
            await c.Quiet();

            Assert.Equal(ChatStatusHandler.Lcf(ChatStatusHandler.Away, "brb").Build(),
                         ChatStatusHandler.Of(CharacterA).Build());

            // Back to available: the message goes with «away».
            using (SessionContext.Push(a.Session))
                await ChatStatusHandler.SetAsync(a.ToClient, Kus(ChatStatusHandler.Available));
            Assert.Equal(ChatStatusHandler.BuildUpdate(CharacterA, ChatStatusHandler.Available, ""), await a.Next(Op.Ktx));
            Assert.Equal("", a.Session.State.ChatStatusMessage);
            Forget();
        }

        [Fact]
        public async Task Offline_and_unknown_statuses_are_not_taken_and_a_long_message_is_refused()
        {
            Forget();
            await using var a = await ClientPipe.OpenAsync(AccountA, CharacterA, Map, "Estado-Uno");

            using (SessionContext.Push(a.Session))
            {
                await ChatStatusHandler.SetAsync(a.ToClient, Kus(ChatStatusHandler.Offline));
                await ChatStatusHandler.SetAsync(a.ToClient, Kus(9));
                Assert.Equal(ChatStatusHandler.Available, a.Session.State.ChatStatus);

                // Neither was answered: the first frame that comes is the refusal below.

                await ChatStatusHandler.SetAsync(a.ToClient,
                    Kus(ChatStatusHandler.Away, new string('x', ChatStatusHandler.MaxMessageLength + 1)));
                Assert.Empty(await a.Next(Op.Kty));
                Assert.Equal(ChatStatusHandler.Available, a.Session.State.ChatStatus);
            }
            Forget();
        }

        [Fact]
        public async Task In_private_only_his_contacts_whisper_to_him()
        {
            Forget();
            await using var a = await ClientPipe.OpenAsync(AccountA, CharacterA, Map, "Estado-Uno");
            await using var b = await ClientPipe.OpenAsync(AccountB, CharacterB, Map, "Estado-Dos");
            await using var c = await ClientPipe.OpenAsync(AccountC, CharacterC, Map, "Estado-Tres");
            Contacts.Add(AccountA, AccountB, Contacts.Contact);

            using (SessionContext.Push(a.Session))
                await ChatStatusHandler.SetAsync(a.ToClient, Kus(ChatStatusHandler.Private));
            await a.Next(Op.Ktx);

            byte[] Whisper(string text) => ConnectionProtocol.Push(Op.Ktb, Pb.New().Str(1, text).Str(5, "Estado-Uno").Build());

            using (SessionContext.Push(c.Session))
                await PrivateMessageHandler.WhisperAsync(c.ToClient, Whisper("hola"));
            await c.Next(Op.Ktl);

            using (SessionContext.Push(b.Session))
                await PrivateMessageHandler.WhisperAsync(b.ToClient, Whisper("hola"));
            await a.Next(Op.Kth);
            await b.Next(Op.Kth);
            Forget();
        }

        [Fact]
        public async Task In_solo_a_party_invitation_does_not_reach_him()
        {
            Forget();
            await using var a = await ClientPipe.OpenAsync(AccountA, CharacterA, Map, "Estado-Uno");
            await using var b = await ClientPipe.OpenAsync(AccountB, CharacterB, Map, "Estado-Dos");

            using (SessionContext.Push(a.Session))
                await ChatStatusHandler.SetAsync(a.ToClient, Kus(ChatStatusHandler.Solo));
            await a.Next(Op.Ktx);

            using (SessionContext.Push(b.Session))
            {
                await PartyHandler.InviteAsync(b.ToClient, ConnectionProtocol.Push(Op.Ime,
                    Pb.New().Msg(1, Pb.New().Msg(4, Pb.New().Str(1, "Estado-Uno"))).Build()));
            }
            Assert.Null(Parties.Of(CharacterB));
            Assert.False(Parties.IsInParty(CharacterA));
            await a.Quiet();
            Forget();
        }
    }
}
