using System;
using System.Collections.Generic;
using System.Linq;
using Jondo.Unity.Server.Managers;

namespace Jondo.Unity.Server.Network
{
    /// <summary>
    /// The guild journal (jim → jil): its four lists and the lines in them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read off the client (fft.behn, dg.mrg, GuildLogBook's Format* and OpenSelectedTab), the
    /// founding's line measured in «muchas acciones». jil is { f1 general, f2 missions, f3 raids,
    /// f5 chest } -- the logbook's tabs 0, 1, 2 and 3; the client also shows the general list's
    /// weekly lines (3, 10, 13) under missions and the missions' rerolls (16) under general.
    /// </para>
    /// <para>
    /// A line (llc) is { f11 the guild, f19 when, and one case of a oneof }: each case below with
    /// the fields the client reads, numbered by the client's own classes (the generated code keeps
    /// each field's number right before it). The actor of a line is its character and his name,
    /// which the client turns into «Has…» for its reader, «{0} ha…» for anybody else.
    /// </para>
    /// </remarks>
    public static class GuildLogProtocol
    {
        // The oneof's cases (llc), each the client's activity class.
        public const int RaidKicked = 2;            // lkx, da
        public const int WeeklyStep = 3;            // lkq, cw
        public const int RaidDescription = 4;       // lla, dd
        public const int PlayerRank = 5;            // lkn, cy
        public const int RaidPurchase = 6;          // lku, db
        public const int RaidCaptain = 7;           // lky, dc
        public const int Chest = 8;                 // lkc, cs
        public const int RaidFinished = 9;          // lkz, cz
        public const int NextWeekly = 10;           // lkp, df
        public const int RankChange = 12;           // lkm, de
        public const int MissionConfiguration = 13; // lkr, cu
        public const int LevelUp = 14;              // lkh, cp
        public const int MissionComplete = 15;      // lks, ct
        public const int MissionReroll = 16;        // lko, cv
        public const int ChestTabUnlocked = 17;     // lki, cr -- the founding's line in the capture
        public const int PlayerFlow = 20;           // lkg, cx

        /// <summary>Somebody in or out (lkg f4): «se ha unido», «ha dejado», «se ha rechazado la candidatura».</summary>
        public enum Flow { Joined = 0, Left = 1, ApplicationRefused = 2 }

        /// <summary>What a chest line did (lkc f9).</summary>
        public enum ChestMove { Deposit = 0, Withdraw = 1, Transfer = 2 }

        /// <summary>What a rank line did (lkm f2).</summary>
        public enum RankAction { Created = 0, Updated = 1, Deleted = 2 }

        /// <summary>The list each case goes in.</summary>
        public static GuildStore.LogTab TabOf(int field) => field switch
        {
            MissionComplete or MissionReroll => GuildStore.LogTab.Missions,
            RaidKicked or RaidDescription or RaidPurchase or RaidCaptain or RaidFinished => GuildStore.LogTab.Raids,
            Chest => GuildStore.LogTab.Chest,
            _ => GuildStore.LogTab.General,
        };

        /// <summary>How many lines of each list are sent: the most recent.</summary>
        public const int LinesPerTab = 100;

        /// <summary>
        /// The journal (jil): each tab's most recent lines. A line's fields go in number order, the
        /// case among them -- the capture's founding is f11, f17, f19 and a member's coming f11, f19, f20.
        /// </summary>
        public static byte[] BuildLog(IEnumerable<GuildStore.LogEntry> entries)
        {
            var jil = Pb.New();
            foreach (var tab in entries.GroupBy(e => e.Tab).OrderBy(g => (int)g.Key))
            {
                foreach (var entry in tab.OrderBy(e => e.WhenMs).TakeLast(LinesPerTab))
                {
                    var line = Pb.New().Var(11, entry.GuildId);
                    if (entry.Field < 19) line.Bytes(entry.Field, entry.Details ?? Array.Empty<byte>());
                    line.Var(19, entry.WhenMs);
                    if (entry.Field > 19) line.Bytes(entry.Field, entry.Details ?? Array.Empty<byte>());
                    jil.Msg((int)tab.Key, line);
                }
            }
            return jil.Build();
        }

        // ─── The lines ──────────────────────────────────────────────────────

        /// <summary>lkg { f2 character, f3 name, f4 what }.</summary>
        public static byte[] PlayerFlowLine(long character, string name, Flow flow)
            => Pb.New().Var(2, character).Str(3, name ?? "").VarIfNotZero(4, (int)flow).Build();

        /// <summary>lkh { f2 the level }.</summary>
        public static byte[] LevelUpLine(int level) => Pb.New().Var(2, level).Build();

        /// <summary>A rank as the lines carry it (ldt): { f2 its name, f5 its id }, as the ranks' jco does.</summary>
        private static Pb Rank(int id, string name) => Pb.New().Str(2, name ?? "").Var(5, id);

        /// <summary>lkn { f1 the rank, f2 who, f3 by whom, f4 who's name, f5 by whom's name }: «{0} ha sido nombrado {1} por {2}».</summary>
        public static byte[] PlayerRankLine(int rankId, string rankName, long target, string targetName, long by, string byName)
            => Pb.New().Msg(1, Rank(rankId, rankName)).Var(2, target).Var(3, by)
                       .Str(4, targetName ?? "").Str(5, byName ?? "").Build();

        /// <summary>lkm { f1 the rank, f2 what was done to it }.</summary>
        public static byte[] RankLine(int rankId, string rankName, RankAction action)
            => Pb.New().Msg(1, Rank(rankId, rankName)).VarIfNotZero(2, (int)action).Build();

        /// <summary>
        /// lkq { f1 the milestone, from 1, f2 the tier }: «nuevo hito», or «el máximo de su actividad
        /// semanal» on the tier's last.
        /// </summary>
        public static byte[] WeeklyStepLine(int milestone, int tier) => Pb.New().Var(1, milestone).Var(2, tier).Build();

        /// <summary>lkp { f2 who, f3 the tier, f5 his name }: «ha modificado la franja de la semana que viene».</summary>
        public static byte[] NextWeeklyLine(long actor, string name, int tier)
            => Pb.New().Var(2, actor).Var(3, tier).Str(5, name ?? "").Build();

        /// <summary>lkr { f1 who, f2 his name }: «ha modificado la configuración de los encargos».</summary>
        public static byte[] MissionConfigurationLine(long actor, string name)
            => Pb.New().Var(1, actor).Str(2, name ?? "").Build();

        /// <summary>lks { f1 who, f2 his name, f3 the mission }.</summary>
        public static byte[] MissionCompleteLine(long actor, string name, int mission)
            => Pb.New().Var(1, actor).Str(2, name ?? "").Var(3, mission).Build();

        /// <summary>lko { f1 the mission that went, f2 who, f3 his name, f4 the one that came }.</summary>
        public static byte[] MissionRerollLine(long actor, string name, int oldMission, int newMission)
            => Pb.New().Var(1, oldMission).Var(2, actor).Str(3, name ?? "").Var(4, newMission).Build();

        /// <summary>
        /// lkc { f2 how many, f3 the item, f4 the tab it left, f5 the tab it reached, f7 his name,
        /// f8 who, f9 what }: «{0} ha depositado/retirado/transferido {1} x {2}…».
        /// </summary>
        public static byte[] ChestLine(long actor, string name, ChestMove move, int quantity, Pb item, int fromTab, int toTab)
            => Pb.New().Var(2, quantity).Msg(3, item).VarIfNotZero(4, fromTab).VarIfNotZero(5, toTab)
                       .Str(7, name ?? "").Var(8, actor).VarIfNotZero(9, (int)move).Build();

        /// <summary>lku { f1 the raid, f3 his name, f4 the raid's uuid, f5 who }: «ha comprado la raid».</summary>
        public static byte[] RaidPurchaseLine(long actor, string name, int raid, string uuid)
            => Pb.New().Var(1, raid).Str(3, name ?? "").Str(4, uuid ?? "").Var(5, actor).Build();

        /// <summary>
        /// lky { f1 the captain, f2 the raid's uuid, f3 the raid, f4 his name, f5 the captain's
        /// name, f6 who }: «ha designado a {1} como capitán de la raid {2}».
        /// </summary>
        public static byte[] RaidCaptainLine(long actor, string name, long captain, string captainName, int raid, string uuid)
            => Pb.New().Var(1, captain).Str(2, uuid ?? "").Var(3, raid).Str(4, name ?? "")
                       .Str(5, captainName ?? "").Var(6, actor).Build();

        /// <summary>lla { f1 who, f2 the raid, f3 his name, f4 the raid's uuid }: «ha modificado la descripción».</summary>
        public static byte[] RaidDescriptionLine(long actor, string name, int raid, string uuid)
            => Pb.New().Var(1, actor).Var(2, raid).Str(3, name ?? "").Str(4, uuid ?? "").Build();

        /// <summary>
        /// lkx { f2 who, f3 the one put out's name, f4 the one put out, f5 the raid's uuid, f6 the
        /// raid, f7 his name }: «ha retirado a {1} de la raid {2}».
        /// </summary>
        public static byte[] RaidKickedLine(long actor, string name, long kicked, string kickedName, int raid, string uuid)
            => Pb.New().Var(2, actor).Str(3, kickedName ?? "").Var(4, kicked).Str(5, uuid ?? "")
                       .Var(6, raid).Str(7, name ?? "").Build();

        /// <summary>lkz { f1 the raid, f4 its uuid }: «la raid {0} ha terminado».</summary>
        public static byte[] RaidFinishedLine(int raid, string uuid) => Pb.New().Var(1, raid).Str(4, uuid ?? "").Build();
    }
}
