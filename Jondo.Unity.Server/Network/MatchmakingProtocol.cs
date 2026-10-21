using System;
using System.Collections.Generic;
using System.Linq;
using Jondo.Unity.Server.Managers;

namespace Jondo.Unity.Server.Network
{
    /// <summary>
    /// The automatic dungeon search's messages. Tags read off the client's own parsers in Ghidra
    /// (kis, kif, kjb, kjl, kkk and their parts), checked against the capture «busqueda automatica
    /// de grupo-encuentra para una dung-aceptar-autoteleporte de todos y creacion de grupo».
    /// </summary>
    public static class MatchmakingProtocol
    {
        /// <summary>What the search asks for (kke): f1 { f1 dungeons } a classic one, f2 a random one, f3 completing a party.</summary>
        public enum Kind { None = 0, Classic = 1, Random = 2, Completing = 3 }

        /// <summary>The client's codes for refusing a search (kjb's error, fgr.bepu).</summary>
        public static class JoinCode
        {
            public const int EmptySlice = 2;
            public const int BadRequiredLevel = 5;
            public const int NoDungeonLeft = 9;
            public const int InvalidRequest = 11;
            public const int NotLeader = 12;
            public const int KeyMissing = 16;
        }

        /// <summary>The client's codes for a yes that cannot be taken (kjv, fgr.beqe).</summary>
        public static class AcceptCode
        {
            public const int Ok = 0;
            public const int PlayerOccupied = 1;
            public const int KeyNeeded = 4;
            public const int NoWaitingInvitation = 5;
        }

        // ─── Requests ───────────────────────────────────────────────────────

        /// <summary>
        /// The search (kke): its kind and, for a classic one, the dungeons -- the capture's 90 ids
        /// packed in f1 { f1 }.
        /// </summary>
        public static (Kind Kind, List<int> Dungeons) ReadSearch(byte[] kke)
        {
            foreach (var field in ProtoMessage.Parse(kke ?? Array.Empty<byte>()).Fields)
            {
                if (field.WireType != 2) continue;
                if (field.FieldNumber == 1) return (Kind.Classic, Ints(field.BytesValue, 1));
                if (field.FieldNumber == 2) return (Kind.Random, new List<int>());
                if (field.FieldNumber == 3) return (Kind.Completing, new List<int>());
            }
            return (Kind.None, new List<int>());
        }

        // ─── Answers and notices ────────────────────────────────────────────

        /// <summary>The search taken (kjb): f2 {}, the capture's «f2 = ''».</summary>
        public static byte[] BuildSearchTaken() => Pb.New().EmptyMsg(2).Build();

        /// <summary>The search refused (kjb): f1 { f2 the code }.</summary>
        public static byte[] BuildSearchRefused(int code) => Pb.New().Msg(1, Pb.New().Var(2, code)).Build();

        /// <summary>
        /// The search refused to somebody barred from it (kjb): f3 { f2 the seconds left } -- the
        /// client's errorJoinBanned, «no puedes participar en la búsqueda automática durante {0}
        /// porque has abandonado tu grupo o tu grupo te ha expulsado» (fgr.bepu's third case).
        /// </summary>
        public static byte[] BuildSearchBanned(long seconds) => Pb.New().Msg(3, Pb.New().Var(2, Math.Max(1, seconds))).Build();

        /// <summary>
        /// Searching (kkk): f1 { f3 the dungeons }, f4 0 -- the capture's, right after the search
        /// is taken.
        /// </summary>
        public static byte[] BuildSearching(IEnumerable<int> dungeons)
            => Pb.New().Msg(1, Pb.New().Packed(3, dungeons.Select(d => (long)d))).Var(4, 0).Build();

        /// <summary>The search stopped (kje): f1 the code, 0 for fine.</summary>
        public static byte[] BuildStopped(int code = 0) => Pb.New().VarIfNotZero(1, code).Build();

        /// <summary>A place in the ready check (kid): f1 1 while waiting, f2 sex, f3 breed.</summary>
        public sealed record Place(bool Waiting, int Sex, int Breed);

        /// <summary>
        /// The ready check (kif): f1 each place, f2 the seconds left, f3 whether this player said
        /// yes, f4 the dungeon -- the capture's { {1,1,15}, {1,_,9}, {1,_,11}, {1,_,9} }, 59, 141.
        /// </summary>
        public static byte[] BuildReadyCheck(IEnumerable<Place> places, long secondsLeft, bool accepted, int dungeonId)
        {
            var kif = Pb.New();
            foreach (var place in places)
                kif.Msg(1, Pb.New().VarIfNotZero(1, place.Waiting ? 1 : 0).VarIfNotZero(2, place.Sex).Var(3, place.Breed));
            return kif.Var(2, Math.Max(0, secondsLeft))
                      .VarIfNotZero(3, accepted ? 1 : 0)
                      .Var(4, dungeonId)
                      .Build();
        }

        /// <summary>The answer to a yes (kjv): f1 the code; the capture's is empty.</summary>
        public static byte[] BuildAccepted(int code = AcceptCode.Ok) => Pb.New().VarIfNotZero(1, code).Build();

        /// <summary>The group made (kjl): f1 { f1 the dungeon } -- the capture's «f1 { f1 141 }».</summary>
        public static byte[] BuildMatched(int dungeonId) => Pb.New().Msg(1, Pb.New().Var(1, dungeonId)).Build();

        /// <summary>The group not made (kjl): f2 {}, the client's «matchFail».</summary>
        public static byte[] BuildMatchFailed() => Pb.New().EmptyMsg(2).Build();

        /// <summary>
        /// The window's state (kis, the answer to kjx): f3 one entry per dungeon the search offers,
        /// { f2 its anomaly, in percent, f5 the dungeon }, by id -- the capture's 118, «{ f2 34, f5 3 }».
        /// It is the classic search's list: a dungeon left out is not in it, which is how an entry
        /// per dungeon somebody waited for showed only those, and their count as the anomaly.
        /// </summary>
        /// <remarks>
        /// The anomaly is the dungeons' own, which this server does not run (Anomalies is the
        /// zaaps' temporal ones), so it goes as 0.
        /// </remarks>
        public static byte[] BuildState(IEnumerable<int> dungeons)
        {
            var kis = Pb.New();
            foreach (int dungeon in dungeons.Distinct().OrderBy(d => d))
                kis.Msg(3, Pb.New().Var(5, dungeon));
            return kis.Build();
        }

        // ─── The group's votes ──────────────────────────────────────────────
        //
        // Read off the client: fgk sends -- the give-up popup kjs { f1 true } (fgk.beov), the kick
        // popup the party's own ili through the ddw event (ffd.bdyc) --, fgr reads -- kig, kkq and
        // kjr for giving up, ikw, ike and iku for putting out, kjo for leaving; the ballots are
        // kjs { f1 yes } (fgr.beql) and ili (yes) or ikn {} (no) (fgr+fgp.bepj, bepi). No capture
        // has any of it.

        /// <summary>A ballot on giving up (kjs): f1 yes.</summary>
        public static bool ReadSurrenderBallot(byte[] kjs)
            => ProtoMessage.Parse(kjs ?? Array.Empty<byte>()).Fields.Any(f => f.FieldNumber == 1 && f.WireType == 0 && f.VarIntValue != 0);

        /// <summary>
        /// The give-up vote (kig): f1 the ballots so far, f2 the seconds left. The client counts
        /// them against the party's size and offers its two buttons to whoever has not voted.
        /// </summary>
        public static byte[] BuildSurrenderVote(IEnumerable<bool> ballots, long secondsLeft)
            => Pb.New().Packed(1, ballots.Select(b => b ? 1L : 0L)).Var(2, Math.Max(0, secondsLeft)).Build();

        /// <summary>The give-up vote over (kkq): f2 what it came to.</summary>
        public static byte[] BuildSurrenderResult(Matchmaking.Outcome outcome)
            => Pb.New().VarIfNotZero(2, (int)outcome).Build();

        /// <summary>No give-up vote yet (kjr): f2 the seconds before another may start.</summary>
        public static byte[] BuildSurrenderCooldown(long seconds) => Pb.New().Var(2, Math.Max(1, seconds)).Build();

        /// <summary>
        /// The vote to put somebody out (ikw): f1 the ballots so far, f2 whom, f3 the MILLISECONDS
        /// left -- the client divides them by a thousand (qx.pnh) where kig's seconds go as they are.
        /// </summary>
        public static byte[] BuildKickVote(IEnumerable<bool> ballots, long target, long millisecondsLeft)
            => Pb.New().Packed(1, ballots.Select(b => b ? 1L : 0L)).Var(2, target).Var(3, Math.Max(0, millisecondsLeft)).Build();

        /// <summary>The vote to put somebody out over (ike): f1 whom, f2 what it came to.</summary>
        public static byte[] BuildKickResult(long target, Matchmaking.Outcome outcome)
            => Pb.New().Var(1, target).VarIfNotZero(2, (int)outcome).Build();

        /// <summary>No vote to put somebody out yet (iku): f1 the seconds before another may start.</summary>
        public static byte[] BuildKickCooldown(long seconds) => Pb.New().Var(1, Math.Max(1, seconds)).Build();

        /// <summary>
        /// Out of a full group by one's own will (kjo): f2 the seconds barred from the search --
        /// «no podrás usar la búsqueda de grupo durante {0} minutos».
        /// </summary>
        public static byte[] BuildLeaveCooldown(long seconds) => Pb.New().Var(2, Math.Max(1, seconds)).Build();

        /// <summary>A repeated int32 in a message's field, packed or not.</summary>
        private static List<int> Ints(byte[] message, int number)
        {
            var found = new List<int>();
            foreach (var field in ProtoMessage.Parse(message ?? Array.Empty<byte>()).Fields)
            {
                if (field.FieldNumber != number) continue;
                if (field.WireType == 0)
                {
                    found.Add((int)field.VarIntValue);
                    continue;
                }
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
                    found.Add((int)value);
                }
            }
            return found;
        }
    }
}
