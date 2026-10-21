using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The treasure hunts the Malle aux trésors hands out: Cano Dhain's quests, the Anutrof box that
    /// launches them, and the chest each one ends in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client's own data: Dhain (NPC 2345) gives the quests on map 128452097 («The Treasure
    /// Chest», [-25,-36]) -- 2539 «Búsqueda del tesoro diaria», 2538 the archaeological one,
    /// 2540-2543 the saints' --, each a single step «Encuentra y abre el cofre…»; his lines send one
    /// to «la caja de Anutrof que está junto a mí», and the skill names 362-367 («Buscar el tesoro
    /// del día»… «Buscar el tesoro de Milivar») are what that box offers. The chests are the items
    /// «Cofre del día» 32382, «Cofre de arqueologista» 32379, «Cofre de Guss» 32383 and so on.
    /// </para>
    /// <para>
    /// DofusPourLesNoobs (Les chasses aux trésors): «3 ou 4 étapes pour une chasse normale», four
    /// tries, and the Treasure fought at the end. INFERRED: the box is element 484993 (the room's
    /// other two, 485495 and 485497, sharing a drawing, are its Pupitre and Mesa de planificación),
    /// its type the client's 233 «Cofre de oro», next to the tower's 232 and the Pupitre's 236; a
    /// stage has five steps, as the captured legendary hunt's; a hunt starts on an outdoor map whose
    /// zone is no higher than the hunter's level.
    /// </para>
    /// </remarks>
    public static class TreasureQuests
    {
        /// <summary>One of Dhain's hunts: its quest, the box's name for it and the chest it ends in.</summary>
        public sealed record Kind(int Quest, int SkillName, int Chest, string Name);

        public static readonly IReadOnlyList<Kind> Kinds = new[]
        {
            new Kind(2539, 362, 32382, "daily"),
            new Kind(2538, 363, 32379, "archaeological"),
            new Kind(2540, 364, 32383, "Guss"),
            new Kind(2541, 365, 32385, "Scheik"),
            new Kind(2542, 366, 32386, "Sad"),
            new Kind(2543, 367, 32384, "Milivar"),
        };

        /// <summary>The Malle aux trésors, Dhain's room, and Dhain.</summary>
        public const long Room = 128452097;
        public const int Dhain = 2345;

        /// <summary>The Anutrof box: its element and type, and its skill, «Utilizar» under the names above.</summary>
        public const int Box = 484993, BoxType = 233, UseSkill = 184;

        /// <summary>
        /// The Pupitre, where a legendary hunt's map is put together from its fragments: «Fabricar un
        /// mapa», skill 275, whose recipes are the maps' (8 fragments for Gein's), job 1 -- «aucun
        /// métier ni prérequis», DPLN. INFERRED: it is 485497, the desk with the magnifier and the
        /// scrolls; the other, with the compass and a map, is the planning table.
        /// </summary>
        public const int Desk = 485497, DeskType = 236, MakeMapSkill = 275;

        /// <summary>The Mesa de planificación: «Buscar un tesoro legendario», skill 277 (hhg, then hhn).</summary>
        public const int PlanningTable = 485495, PlanningTableType = 237, LegendarySkill = 277;

        /// <summary>«3 ou 4 étapes pour une chasse normale»; five steps a stage.</summary>
        public const int FewestStages = 3, MostStages = 4, StepsPerStage = 5;

        /// <summary>The lowest zone a hunt starts in, whatever the hunter's level: the quest asks for 20.</summary>
        public const int LowestLevel = 20;

        public static Kind? OfQuest(int quest) => Kinds.FirstOrDefault(k => k.Quest == quest);

        public static Kind? OfName(int skillName) => Kinds.FirstOrDefault(k => k.SkillName == skillName);

        public static Kind? OfChest(int item) => Kinds.FirstOrDefault(k => k.Chest == item);

        /// <summary>
        /// The Treasure's grade for a hunter: the highest whose level he has reached, the first when
        /// none -- the client's grades go from 20 to 200, by twenty.
        /// </summary>
        public static int TreasureGrade(IReadOnlyList<int> gradeLevels, int level)
        {
            int best = 0;
            for (int i = 0; i < gradeLevels.Count; i++)
                if (gradeLevels[i] <= level) best = i;
            return best;
        }

        /// <summary>Whether a hunt can start on a map: its zone's level within the hunter's.</summary>
        public static bool StartsFor(int zoneLevel, int level) => zoneLevel <= Math.Max(LowestLevel, level);

        // ─── One a day ──────────────────────────────────────────────────────

        /// <summary>When a hunting day starts: «une fois par jour à partir de 7h», on Paris's clock.</summary>
        public const int DayStartsAtHour = 7;

        private static readonly TimeZoneInfo Paris = FindParis();

        private static TimeZoneInfo FindParis()
        {
            foreach (string id in new[] { "Europe/Paris", "Romance Standard Time" })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            return TimeZoneInfo.Utc;
        }

        /// <summary>The hunting day a moment falls on: the Paris date, the day starting at 7:00.</summary>
        public static string DayOf(DateTimeOffset moment)
            => TimeZoneInfo.ConvertTime(moment, Paris).DateTime.AddHours(-DayStartsAtHour).ToString("yyyy-MM-dd");

        private static bool _tableReady;

        /// <summary>The hunting day a character last drew this quest's map from the box, or "".</summary>
        public static string HuntedOn(long characterId, int quest)
        {
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT Day FROM CharacterTreasureDays WHERE CharacterId = $c AND QuestId = $q;";
                command.Parameters.AddWithValue("$c", characterId);
                command.Parameters.AddWithValue("$q", quest);
                return command.ExecuteScalar() as string ?? "";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Hunts] Could not read the hunting days of {characterId}: {ex.Message}");
                return "";
            }
        }

        /// <summary>A quest's map drawn from the box on that hunting day.</summary>
        public static void MarkHunted(long characterId, int quest, string day)
        {
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO CharacterTreasureDays (CharacterId, QuestId, Day) VALUES ($c, $q, $d) " +
                                      "ON CONFLICT(CharacterId, QuestId) DO UPDATE SET Day = excluded.Day;";
                command.Parameters.AddWithValue("$c", characterId);
                command.Parameters.AddWithValue("$q", quest);
                command.Parameters.AddWithValue("$d", day);
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Hunts] Could not keep the hunting day of {characterId}: {ex.Message}");
            }
        }

        private static void EnsureTable()
        {
            if (_tableReady) return;
            using var connection = Open();
            using var create = connection.CreateCommand();
            create.CommandText = @"
                CREATE TABLE IF NOT EXISTS CharacterTreasureDays (
                    CharacterId INTEGER NOT NULL,
                    QuestId     INTEGER NOT NULL,
                    Day         TEXT    NOT NULL,
                    PRIMARY KEY (CharacterId, QuestId));";
            create.ExecuteNonQuery();
            _tableReady = true;
        }

        private static SqliteConnection Open()
        {
            var connection = new SqliteConnection(DatabaseManager.WorldConnectionString);
            connection.Open();
            return connection;
        }
    }
}
