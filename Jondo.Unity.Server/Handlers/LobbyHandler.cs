using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;

namespace Jondo.Unity.Server.Handlers
{
    /// <summary>
    /// The party search («Búsqueda de grupo», the client's lobbies): the board, posting, editing
    /// and taking down an announcement, applying, leaving, and the owner's answers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What the capture shows is followed as it goes: the board answers each jmu; posting answers
    /// with the uid; joining an announcement that takes anyone sends the member in (jpk), an empty
    /// jnu and the answer jnn 1; leaving it sends the member out (jor) and an empty jol; taking
    /// one's own down sends the jnz before the jnk.
    /// </para>
    /// <para>
    /// The owner's side is not in any capture; it is the client's: a new applicant reaches him in a
    /// jpq, his answer goes back in a jom and is answered with a jpe, the applicant hears it in a
    /// jmv, and the rejected stay rejected until he throws them away (joh, answered with a jpb).
    /// </para>
    /// <para>
    /// Who hears the changes: the announcement's members and applicants, and whoever has the tab
    /// open -- it is opened with a jmu and closed with a jop.
    /// </para>
    /// </remarks>
    public static class LobbyHandler
    {
        private static readonly ConcurrentDictionary<long, byte> _watching = new();

        private static Task WriteAsync(NetworkStream stream, byte[] frame)
            => Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, frame);

        private static bool Online(long characterId) => SessionRegistry.FindByCharacter(characterId) != null;

        /// <summary>The player as the board shows him, now.</summary>
        private static Lobbies.Player Me()
        {
            long id = SessionContext.State.CharacterId;
            var character = id == 0 ? null : DatabaseManager.GetCharacterById(id);
            if (character == null) return null;
            int level = GameState.CharacterLevel > 0 ? GameState.CharacterLevel : character.Level;
            return new Lobbies.Player(id, Lobbies.UidOf(id), character.Name, character.Breed, level, character.Sex, DateTime.UtcNow);
        }

        /// <summary>Whoever hears an announcement's changes: its people and the tab's watchers.</summary>
        private static List<long> AudienceOf(Lobbies.Lobby lobby)
            => Lobbies.Read(() => lobby.Members.Select(m => m.CharacterId)
                                       .Concat(lobby.Applicants.Select(a => a.CharacterId))
                                       .Concat(_watching.Keys)
                                       .Distinct()
                                       .ToList());

        private static async Task TellAsync(IEnumerable<long> characters, byte[] frame)
        {
            foreach (long id in characters.Distinct())
            {
                var session = SessionRegistry.FindByCharacter(id);
                if (session == null) continue;
                try
                {
                    await session.SendAsync(frame);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Lobbies] A notice could not reach {id}: {ex.Message}");
                }
            }
        }

        /// <summary>The board (jmu → jpx): every announcement whose owner is on, and the player's waiting applications.</summary>
        public static async Task BoardAsync(NetworkStream stream, byte[] frame)
        {
            long who = SessionContext.State.CharacterId;
            if (who == 0) return;
            _watching[who] = 0;
            byte[] board = Lobbies.Read(() => LobbyProtocol.BuildBoard(Lobbies.Board(Online), Lobbies.AppliedBy(who)));
            await WriteAsync(stream, ConnectionProtocol.Answer(Op.Jpx, board, ConnectionProtocol.RequestId(frame)));
        }

        /// <summary>The tab closed (jop → jns): he no longer hears the board's changes.</summary>
        public static async Task CloseAsync(NetworkStream stream, byte[] frame)
        {
            _watching.TryRemove(SessionContext.State.CharacterId, out _);
            await WriteAsync(stream, ConnectionProtocol.Answer(Op.Jns, Array.Empty<byte>(), ConnectionProtocol.RequestId(frame)));
        }

        /// <summary>His own announcement (jpr → jng), with its applicants and rejected.</summary>
        public static async Task MineAsync(NetworkStream stream, byte[] frame)
        {
            long who = SessionContext.State.CharacterId;
            if (who == 0) return;
            byte[] mine = Lobbies.Read(() => LobbyProtocol.BuildMine(Lobbies.OwnedBy(who)));
            await WriteAsync(stream, ConnectionProtocol.Answer(Op.Jng, mine, ConnectionProtocol.RequestId(frame)));
        }

        /// <summary>Posting (jnv → joq).</summary>
        public static async Task PostAsync(NetworkStream stream, byte[] frame)
        {
            var me = Me();
            byte[] jnv = ConnectionProtocol.ReadPayload(frame, Op.Jnv);
            if (me == null || jnv == null) return;
            var (code, lobby) = Lobbies.Create(me, LobbyProtocol.ReadSpec(jnv));
            if (lobby != null)
                Console.WriteLine($"[Lobbies] {me.Name} posts a party search: kind {lobby.Type}, activity {lobby.ActivityId}, " +
                                  $"{lobby.GroupSize} players{(lobby.AutoJoin ? ", open" : "")}.");
            await WriteAsync(stream, ConnectionProtocol.Answer(Op.Joq,
                LobbyProtocol.BuildPosted(code, lobby?.Uid), ConnectionProtocol.RequestId(frame)));
        }

        /// <summary>Editing (jov → jps), and the new text and tags to whoever hears it (jnt).</summary>
        public static async Task EditAsync(NetworkStream stream, byte[] frame)
        {
            long who = SessionContext.State.CharacterId;
            byte[] jov = ConnectionProtocol.ReadPayload(frame, Op.Jov);
            if (who == 0 || jov == null) return;
            var (tags, text) = LobbyProtocol.ReadEdit(jov);
            var (code, lobby) = Lobbies.Edit(who, tags, text);
            await WriteAsync(stream, ConnectionProtocol.Answer(Op.Jps, LobbyProtocol.BuildEdited(code), ConnectionProtocol.RequestId(frame)));
            if (lobby != null)
                await TellAsync(AudienceOf(lobby), ConnectionProtocol.Push(Op.Jnt, Lobbies.Read(() => LobbyProtocol.BuildEditNotice(lobby))));
        }

        /// <summary>Taking his announcement down (jog → jnz to its people, then jnk).</summary>
        public static async Task DeleteAsync(NetworkStream stream, byte[] frame)
        {
            long who = SessionContext.State.CharacterId;
            string uid = LobbyProtocol.ReadString(ConnectionProtocol.ReadPayload(frame, Op.Jog), 1);
            if (who == 0 || uid == null) return;
            var audience = Lobbies.ByUid(uid) is { } before ? AudienceOf(before) : new List<long>();
            var (code, lobby) = Lobbies.Delete(who, uid);
            if (lobby != null)
                await TellAsync(audience.Append(who), ConnectionProtocol.Push(Op.Jnz, LobbyProtocol.BuildGone(lobby.Uid)));
            await WriteAsync(stream, ConnectionProtocol.Answer(Op.Jnk, LobbyProtocol.BuildDeleted(code), ConnectionProtocol.RequestId(frame)));
        }

        /// <summary>
        /// Applying (joc → jnn): in at once to an open announcement (jpk to its people, jnu to him),
        /// or waiting, which its owner hears in a jpq.
        /// </summary>
        public static async Task ApplyAsync(NetworkStream stream, byte[] frame)
        {
            var me = Me();
            string uid = LobbyProtocol.ReadString(ConnectionProtocol.ReadPayload(frame, Op.Joc), 1);
            if (me == null || uid == null) return;
            var (code, lobby) = Lobbies.Apply(me, uid);
            if (code == Lobbies.ApplyCode.Joined)
            {
                await TellAsync(AudienceOf(lobby), ConnectionProtocol.Push(Op.Jpk, LobbyProtocol.BuildMemberIn(me, lobby.Uid)));
                await WriteAsync(stream, ConnectionProtocol.Push(Op.Jnu, Array.Empty<byte>()));
            }
            else if (code == Lobbies.ApplyCode.Applied)
            {
                await TellAsync(new[] { lobby.Owner.CharacterId }, ConnectionProtocol.Push(Op.Jpq, LobbyProtocol.BuildNewApplicant(me, lobby.Uid)));
            }
            await WriteAsync(stream, ConnectionProtocol.Answer(Op.Jnn, LobbyProtocol.BuildApplied(code), ConnectionProtocol.RequestId(frame)));
        }

        /// <summary>Leaving, or taking an application back (jny → jor to its people or jpj to the owner, then jol).</summary>
        public static async Task LeaveAsync(NetworkStream stream, byte[] frame)
        {
            long who = SessionContext.State.CharacterId;
            string uid = LobbyProtocol.ReadString(ConnectionProtocol.ReadPayload(frame, Op.Jny), 1);
            if (who == 0 || uid == null) return;
            var (code, lobby, was, wasMember) = Lobbies.Leave(who, uid);
            if (code == Lobbies.LeaveCode.Ok && wasMember)
                await TellAsync(AudienceOf(lobby).Append(who), ConnectionProtocol.Push(Op.Jor, LobbyProtocol.BuildMemberOut(lobby.Uid, was.Uid)));
            else if (code == Lobbies.LeaveCode.Ok)
                await TellAsync(new[] { lobby.Owner.CharacterId }, ConnectionProtocol.Push(Op.Jpj, LobbyProtocol.BuildApplicantGone(was)));
            await WriteAsync(stream, ConnectionProtocol.Answer(Op.Jol, LobbyProtocol.BuildLeft(code), ConnectionProtocol.RequestId(frame)));
        }

        /// <summary>
        /// The owner's answer (jom → jpe): the applicant hears it (jmv), and an accepted one is a
        /// member to everybody (jpk).
        /// </summary>
        public static async Task AnswerAsync(NetworkStream stream, byte[] frame)
        {
            long who = SessionContext.State.CharacterId;
            byte[] jom = ConnectionProtocol.ReadPayload(frame, Op.Jom);
            if (who == 0 || jom == null) return;
            var (applicantUid, accept) = LobbyProtocol.ReadAnswer(jom);
            var (code, lobby, applicant) = Lobbies.Answer(who, applicantUid, accept);
            await WriteAsync(stream, ConnectionProtocol.Answer(Op.Jpe, LobbyProtocol.BuildAnswered(code, applicantUid),
                                                               ConnectionProtocol.RequestId(frame)));
            if (code != Lobbies.AnswerCode.Accepted && code != Lobbies.AnswerCode.Rejected) return;

            bool accepted = code == Lobbies.AnswerCode.Accepted;
            await TellAsync(new[] { applicant.CharacterId },
                ConnectionProtocol.Push(Op.Jmv, LobbyProtocol.BuildApplicationAnswered(lobby.Uid, accepted)));
            if (accepted)
                await TellAsync(AudienceOf(lobby), ConnectionProtocol.Push(Op.Jpk, LobbyProtocol.BuildMemberIn(applicant, lobby.Uid)));
        }

        /// <summary>Throwing rejected applicants away (joh → jpb): they may apply again.</summary>
        public static async Task FreeAsync(NetworkStream stream, byte[] frame)
        {
            long who = SessionContext.State.CharacterId;
            byte[] joh = ConnectionProtocol.ReadPayload(frame, Op.Joh);
            if (who == 0 || joh == null) return;
            var freed = Lobbies.Free(who, LobbyProtocol.ReadUids(joh));
            await WriteAsync(stream, ConnectionProtocol.Answer(Op.Jpb, LobbyProtocol.BuildFreed(freed), ConnectionProtocol.RequestId(frame)));
        }
    }
}
