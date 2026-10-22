using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.Protocol;

namespace Jondo.Unity.Server.Handlers
{
    /// <summary>
    /// The chat status: available, away (with a message), idle, private or solo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No capture changes it, so all of this is read from the client. The status is the client's
    /// enum dyg, and the wire's lcf.f1 carries it as it is: eak::usm writes the dyg it is given
    /// straight into the lcf. PartyMember::SetPlayerStatus names the values -- 1 says nothing
    /// (available), 2 ui.chat.status.away, 3 idle, 4 private, 5 solo -- and 0 is the offline the
    /// member lists already send.
    /// </para>
    /// <para>
    /// The player asks with kus. eak::usm builds { f2 lcf { f1 status } }; the chat commands that
    /// take a message build f1 instead, with the text in the lcf's f2. The answer is ktx
    /// { f1 lcf, f2 character }: fft::behc posts it to the chat as a ckm (the line
    /// ui.chat.status.statuschange) and redraws the member whose id is f2, and the guild's and
    /// the party's frames listen to the same message. So it goes to the player and to whoever has
    /// him on a list: his party, his guild and the accounts that keep him among their friends.
    /// </para>
    /// <para>
    /// What the statuses do is the game's: «private» lets only the player's friends whisper to
    /// him, and «solo» turns away party invitations. Every list that shows a connected player
    /// shows his status through <see cref="Of(long)"/>.
    /// </para>
    /// </remarks>
    public static class ChatStatusHandler
    {
        /// <summary>The client's statuses (dyg), the same numbers lcf.f1 carries.</summary>
        public const int Offline = 0, Available = 1, Away = 2, Idle = 3, Private = 4, Solo = 5;

        /// <summary>
        /// The longest «away» message kept. The client sends whatever follows the command; past
        /// this the answer is kty, the client's ui.chat.status.awaymessageerror.
        /// </summary>
        public const int MaxMessageLength = 255;

        public static async Task SetAsync(NetworkStream stream, byte[] payload)
        {
            byte[]? kus = ConnectionProtocol.ReadPayload(payload, Op.Kus);
            if (kus == null) return;

            var (status, message) = Read(kus);
            if (status < Available || status > Solo) return;

            if (message.Length > MaxMessageLength)
            {
                await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, ConnectionProtocol.Push(Op.Kty, Array.Empty<byte>()));
                return;
            }

            var session = SessionContext.Current;
            var state = session.State;
            state.ChatStatus = status;
            state.ChatStatusMessage = status == Away ? message : "";

            byte[] ktx = ConnectionProtocol.Push(Op.Ktx, BuildUpdate(state.CharacterId, status, state.ChatStatusMessage));
            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, ktx);
            foreach (var other in AudienceOf(session))
            {
                try { await other.SendAsync(ktx); }
                catch (Exception) { /* gone meanwhile */ }
            }

            Console.WriteLine($"[ChatStatus] {state.CharacterName} → {status}" +
                              (state.ChatStatusMessage.Length > 0 ? $" «{state.ChatStatusMessage}»" : ""));
        }

        /// <summary>kus: f1 or f2, each an lcf { f1 status, f2 message }; the last one present wins.</summary>
        public static (int Status, string Message) Read(byte[] kus)
        {
            int status = -1;
            string message = "";
            foreach (var field in ProtoMessage.Parse(kus).Fields)
            {
                if ((field.FieldNumber != 1 && field.FieldNumber != 2) || field.WireType != 2) continue;
                status = Offline;
                message = "";
                foreach (var inner in ProtoMessage.Parse(field.BytesValue).Fields)
                {
                    if (inner.FieldNumber == 1 && inner.WireType == 0) status = (int)inner.VarIntValue;
                    else if (inner.FieldNumber == 2 && inner.WireType == 2)
                        message = System.Text.Encoding.UTF8.GetString(inner.BytesValue).Trim();
                }
            }
            return (status, message);
        }

        /// <summary>ktx { f1 lcf { f1 status, f2 message }, f2 the character }.</summary>
        public static byte[] BuildUpdate(long characterId, int status, string message)
            => Pb.New().Msg(1, Lcf(status, message)).Var(2, characterId).Build();

        /// <summary>An lcf: f1 the status, f2 the «away» message; offline is the empty block.</summary>
        public static Pb Lcf(int status, string message = "")
            => Pb.New().VarIfNotZero(1, status).StrIfNotEmpty(2, message);

        /// <summary>A character's lcf as the lists show it: his status while connected, offline if not.</summary>
        public static Pb Of(long characterId) => Of(SessionRegistry.FindByCharacter(characterId)?.State);

        /// <summary>
        /// The lcf of a character a list already counts as connected: his status, or available when
        /// his session is not at hand (a list built for someone else's screen, a test).
        /// </summary>
        public static Pb Online(long characterId)
        {
            var state = SessionRegistry.FindByCharacter(characterId)?.State;
            return state == null ? Lcf(Available) : Of(state);
        }

        /// <summary>The lcf of a session's character; offline without one.</summary>
        public static Pb Of(SessionState? state)
            => state == null ? Lcf(Offline) : Lcf(state.ChatStatus, state.ChatStatusMessage);

        /// <summary>
        /// Whether a whisper from this account reaches the player: in «private» only the accounts
        /// he keeps among his contacts do.
        /// </summary>
        public static bool HearsWhisperFrom(GameSession target, long fromAccountId)
            => target.State.ChatStatus != Private || Contacts.IsContact(target.AccountId, fromAccountId);

        /// <summary>Whether the player takes party invitations: not in «solo».</summary>
        public static bool TakesInvitations(GameSession target) => target.State.ChatStatus != Solo;

        /// <summary>Who has the player on a list: his party, his guild and those who keep him as a friend.</summary>
        public static IReadOnlyList<GameSession> AudienceOf(GameSession session)
        {
            long characterId = session.State.CharacterId;
            var characters = new HashSet<long>();

            var party = Parties.Of(characterId);
            if (party != null) characters.UnionWith(Parties.MembersOf(party));

            try
            {
                var guild = GuildStore.GuildOf(characterId);
                if (guild != null) characters.UnionWith(GuildStore.Members(guild.Id).Select(m => m.CharacterId));
            }
            catch (Exception) { /* no guild table yet */ }

            var accounts = new HashSet<long>(Contacts.ListedBy(session.AccountId));
            characters.Remove(characterId);

            return SessionRegistry.InWorld()
                .Where(other => other != session
                                && (characters.Contains(other.State.CharacterId) || accounts.Contains(other.AccountId)))
                .ToList();
        }
    }
}
