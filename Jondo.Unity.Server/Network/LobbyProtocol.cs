using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Jondo.Unity.Server.Managers;

namespace Jondo.Unity.Server.Network
{
    /// <summary>
    /// The party search's messages, as the client reads and writes them. Field numbers are the
    /// client classes' own (clientscan enum), checked against the capture «busqueda de grupo-crear
    /// anuncio-postularse a otro-autoeliminacion de mi anuncio».
    /// </summary>
    public static class LobbyProtocol
    {
        /// <summary>The dates go as the capture's: «2026-08-12T22:40:21.948918561Z».</summary>
        public static string Date(DateTime utc) => utc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

        // ─── Blocks ─────────────────────────────────────────────────────────

        /// <summary>
        /// A player (joa): f1 character, f2 since when, f3 breed, f4 level, f5 his uid on the board,
        /// f6 sex, f7 name -- the capture's «302677754146, …, 11, 354, "6917336a-…", "Sacri-Master"».
        /// </summary>
        public static Pb PlayerBlock(Lobbies.Player player)
            => Pb.New()
                .Var(1, player.CharacterId)
                .Str(2, Date(player.Since))
                .Var(3, player.Breed)
                .Var(4, player.Level)
                .Str(5, player.Uid)
                .VarIfNotZero(6, player.Sex)
                .Str(7, player.Name);

        /// <summary>
        /// An announcement (jos), as LobbyWrapper reads it: f1 kind, f2 activity, f4 group size,
        /// f5 anyone may join, f6 uid, f7 the achievements asked, f8 the tags, f9 when, f10 minimum
        /// level, f11 owner, f12 members (the owner first), f13 the text. The capture's own:
        /// { 1, 92, 2, 1, uid, [704], [2], date, 20, owner, owner }.
        /// </summary>
        public static Pb LobbyBlock(Lobbies.Lobby lobby)
        {
            var jos = Pb.New()
                .Var(1, lobby.Type)
                .Var(2, lobby.ActivityId)
                .Var(4, lobby.GroupSize)
                .VarIfNotZero(5, lobby.AutoJoin ? 1 : 0)
                .Str(6, lobby.Uid);
            if (lobby.Successes.Count > 0) jos.Packed(7, lobby.Successes.Select(s => (long)s));
            if (lobby.Tags.Count > 0) jos.Packed(8, lobby.Tags.Select(t => (long)t));
            jos.Str(9, Date(lobby.Created))
               .VarIfNotZero(10, lobby.MinLevel)
               .Msg(11, PlayerBlock(lobby.Owner));
            foreach (var member in lobby.Members) jos.Msg(12, PlayerBlock(member));
            return jos.StrIfNotEmpty(13, lobby.Description);
        }

        // ─── Answers ────────────────────────────────────────────────────────

        /// <summary>
        /// The board (jpx, the answer to jmu): f1 { f1 each announcement, f2 each of the player's
        /// waiting applications { f2 1, f3 the announcement } } -- fgr.beqs puts f2 1 among the
        /// applied ones.
        /// </summary>
        public static byte[] BuildBoard(IEnumerable<Lobbies.Lobby> lobbies, IEnumerable<Lobbies.Lobby> appliedTo)
        {
            var jpv = Pb.New();
            foreach (var lobby in lobbies) jpv.Msg(1, LobbyBlock(lobby));
            foreach (var lobby in appliedTo) jpv.Msg(2, Pb.New().Var(2, 1).Str(3, lobby.Uid));
            return Pb.New().Msg(1, jpv).Build();
        }

        /// <summary>
        /// The player's own announcement (jng, the answer to jpr): f1 { f1 the applicants, f4 the
        /// announcement, f5 the rejected } -- fgr.beqz reads only f1, and clears the owner's state
        /// when it is missing.
        /// </summary>
        public static byte[] BuildMine(Lobbies.Lobby owned)
        {
            if (owned == null) return Array.Empty<byte>();
            var jpn = Pb.New();
            foreach (var applicant in owned.Applicants) jpn.Msg(1, PlayerBlock(applicant));
            jpn.Msg(4, LobbyBlock(owned));
            foreach (var rejected in owned.Rejected) jpn.Msg(5, PlayerBlock(rejected));
            return Pb.New().Msg(1, jpn).Build();
        }

        /// <summary>Posting (joq, the answer to jnv): f1 the code, f2 the new announcement's uid.</summary>
        public static byte[] BuildPosted(int code, string uid)
            => Pb.New().VarIfNotZero(1, code).StrIfNotEmpty(2, uid).Build();

        /// <summary>Editing (jps, the answer to jov): f1 the code.</summary>
        public static byte[] BuildEdited(int code) => Pb.New().VarIfNotZero(1, code).Build();

        /// <summary>Deleting (jnk, the answer to jog): f1 the code.</summary>
        public static byte[] BuildDeleted(int code) => Pb.New().VarIfNotZero(1, code).Build();

        /// <summary>Applying (jnn, the answer to joc): f1 0 applied, 1 joined at once -- the capture's «f1 1».</summary>
        public static byte[] BuildApplied(int code) => Pb.New().VarIfNotZero(1, code).Build();

        /// <summary>Leaving (jol, the answer to jny): its code in f3, which fgr.beqv reads; the capture's is empty.</summary>
        public static byte[] BuildLeft(int code) => Pb.New().VarIfNotZero(3, code).Build();

        /// <summary>The owner's answer (jpe, the answer to jom): f1 0 rejected, 1 accepted, f2 the applicant.</summary>
        public static byte[] BuildAnswered(int code, string applicantUid)
            => Pb.New().VarIfNotZero(1, code).StrIfNotEmpty(2, applicantUid).Build();

        /// <summary>
        /// Throwing rejected applicants away (jpb, the answer to joh): f1 { f1 each { f1 character,
        /// f2 done, f3 uid } }, which fgr.berb takes off the owner's list.
        /// </summary>
        public static byte[] BuildFreed(IEnumerable<Lobbies.Player> freed)
        {
            var jpa = Pb.New();
            foreach (var player in freed)
                jpa.Msg(1, Pb.New().Var(1, player.CharacterId).Var(2, 1).Str(3, player.Uid));
            return Pb.New().Msg(1, jpa).Build();
        }

        // ─── Notices ────────────────────────────────────────────────────────

        /// <summary>An announcement taken down (jnz): f1 its uid -- «ya no está disponible» to its people.</summary>
        public static byte[] BuildGone(string uid) => Pb.New().Str(1, uid).Build();

        /// <summary>An announcement edited (jnt): f1 its uid, f2 its text, f3 its tags.</summary>
        public static byte[] BuildEditNotice(Lobbies.Lobby lobby)
        {
            var jnt = Pb.New().Str(1, lobby.Uid).Str(2, lobby.Description ?? "");
            if (lobby.Tags.Count > 0) jnt.Packed(3, lobby.Tags.Select(t => (long)t));
            return jnt.Build();
        }

        /// <summary>A member in (jpk): f1 the player, f2 the announcement -- the capture's, on joining.</summary>
        public static byte[] BuildMemberIn(Lobbies.Player player, string lobbyUid)
            => Pb.New().Msg(1, PlayerBlock(player)).Str(2, lobbyUid).Build();

        /// <summary>A member out (jor): f1 the announcement, f3 his uid -- the capture's, on leaving.</summary>
        public static byte[] BuildMemberOut(string lobbyUid, string playerUid)
            => Pb.New().Str(1, lobbyUid).Str(3, playerUid).Build();

        /// <summary>A new applicant, to the owner (jpq): f1 the player, f2 the announcement.</summary>
        public static byte[] BuildNewApplicant(Lobbies.Player player, string lobbyUid)
            => Pb.New().Msg(1, PlayerBlock(player)).Str(2, lobbyUid).Build();

        /// <summary>An applicant who took his application back, to the owner (jpj): f1 the player.</summary>
        public static byte[] BuildApplicantGone(Lobbies.Player player) => Pb.New().Msg(1, PlayerBlock(player)).Build();

        /// <summary>The answer to an application, to the applicant (jmv): f1 the announcement, f2 accepted.</summary>
        public static byte[] BuildApplicationAnswered(string lobbyUid, bool accepted)
            => Pb.New().Str(1, lobbyUid).VarIfNotZero(2, accepted ? 1 : 0).Build();

        // ─── Requests ───────────────────────────────────────────────────────

        /// <summary>
        /// The creation form (jnv), as fgk.beog fills it from the LobbyWrapper: f1 tags, f2 a flag,
        /// f3 kind, f4 text, f5 group size, f6 achievements, f7 activity, f8 anyone may join, f9
        /// minimum level. The capture's: { [2], 1, 2, [704], 92, 1, 20 }.
        /// </summary>
        public static Lobbies.Spec ReadSpec(byte[] jnv)
        {
            var tags = new List<int>();
            var successes = new List<int>();
            int type = 0, size = 0, activity = 0, minLevel = 0;
            bool autoJoin = false;
            string text = "";
            foreach (var field in ProtoMessage.Parse(jnv ?? Array.Empty<byte>()).Fields)
            {
                switch (field.FieldNumber)
                {
                    case 1: Ints(field, tags); break;
                    case 3: type = (int)field.VarIntValue; break;
                    case 4: text = Text(field); break;
                    case 5: size = (int)field.VarIntValue; break;
                    case 6: Ints(field, successes); break;
                    case 7: activity = (int)field.VarIntValue; break;
                    case 8: autoJoin = field.VarIntValue != 0; break;
                    case 9: minLevel = (int)field.VarIntValue; break;
                }
            }
            return new Lobbies.Spec(type, activity, size, autoJoin, tags, successes, minLevel, text);
        }

        /// <summary>The edit (jov): f1 the tags, f2 the text.</summary>
        public static (List<int> Tags, string Text) ReadEdit(byte[] jov)
        {
            var tags = new List<int>();
            string text = "";
            foreach (var field in ProtoMessage.Parse(jov ?? Array.Empty<byte>()).Fields)
            {
                if (field.FieldNumber == 1) Ints(field, tags);
                else if (field.FieldNumber == 2 && field.WireType == 2) text = Text(field);
            }
            return (tags, text);
        }

        /// <summary>The string in a field: the uid of jog, joc and jny is their f1.</summary>
        public static string ReadString(byte[] payload, int number)
        {
            var field = ProtoMessage.Parse(payload ?? Array.Empty<byte>()).Fields
                                    .FirstOrDefault(f => f.FieldNumber == number && f.WireType == 2);
            return field == null ? null : Text(field);
        }

        /// <summary>The owner's answer (jom): f1 the applicant's uid, f2 accepted.</summary>
        public static (string Uid, bool Accept) ReadAnswer(byte[] jom)
        {
            string uid = null;
            bool accept = false;
            foreach (var field in ProtoMessage.Parse(jom ?? Array.Empty<byte>()).Fields)
            {
                if (field.FieldNumber == 1 && field.WireType == 2) uid = Text(field);
                else if (field.FieldNumber == 2 && field.WireType == 0) accept = field.VarIntValue != 0;
            }
            return (uid, accept);
        }

        /// <summary>The rejected thrown away (joh): f1 each uid.</summary>
        public static List<string> ReadUids(byte[] joh)
            => ProtoMessage.Parse(joh ?? Array.Empty<byte>()).Fields
                           .Where(f => f.FieldNumber == 1 && f.WireType == 2).Select(Text).ToList();

        private static string Text(ProtoField field) => Encoding.UTF8.GetString(field.BytesValue ?? Array.Empty<byte>());

        /// <summary>A repeated int32, packed or not.</summary>
        private static void Ints(ProtoField field, List<int> into)
        {
            if (field.WireType == 0)
            {
                into.Add((int)field.VarIntValue);
                return;
            }
            if (field.WireType != 2) return;
            var bytes = field.BytesValue ?? Array.Empty<byte>();
            int i = 0;
            while (i < bytes.Length)
            {
                ulong value = 0;
                int shift = 0;
                while (i < bytes.Length)
                {
                    byte b = bytes[i++];
                    value |= (ulong)(b & 0x7F) << shift;
                    if ((b & 0x80) == 0) break;
                    shift += 7;
                }
                into.Add((int)value);
            }
        }
    }
}
