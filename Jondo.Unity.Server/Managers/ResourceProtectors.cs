using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Jondo.Unity.Server.Network;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The resources' protectors: a harvest may bring out the guardian of what was gathered, and
    /// it attacks there and then.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client's data has them and says whose each one is: five races -- «Protectores de
    /// cereales» (62), «de minerales» (63), «de árboles» (64), «de peces» (65) and «de plantas»
    /// (66) --, one monster per resource, «Espantapájaros de trigo», «Crujibola de hierro», with ten
    /// grades from level 10 to 190. Each drops the sack of its resource, «Saco de trigo», whose
    /// effect 209 carries the resource (289, the wheat): that is how a resource finds its protector.
    /// </para>
    /// <para>
    /// From the job guides (JOL, «Mineur», «Paysan»): they come out «aléatoirement une fois le
    /// niveau 20 d'un métier de récolte atteint», and the sack holds «50 unités de la ressource».
    /// The Almanax's day 292 makes them «5 veces más» likely (type 9, 400).
    /// INFERRED: one harvest in a hundred, and the grade the highest whose level the job's
    /// reaches -- the guides give neither.
    /// </para>
    /// </remarks>
    public static class ResourceProtectors
    {
        /// <summary>The protectors' races in the client's MonsterRaces.</summary>
        private static readonly HashSet<int> Races = new() { 62, 63, 64, 65, 66 };

        /// <summary>The sack's effect that names the resource it holds.</summary>
        private const int SackHolds = 209;

        /// <summary>The job level from which they come out.</summary>
        public const int FromJobLevel = 20;

        /// <summary>One harvest in this many brings one out, before the Almanax.</summary>
        public const int OneIn = 100;

        private static readonly object _lock = new();
        private static Dictionary<int, int>? _byResource;

        /// <summary>The protector of a resource item, or 0.</summary>
        public static int ProtectorOf(int resourceItem)
        {
            Load();
            return _byResource!.TryGetValue(resourceItem, out int protector) ? protector : 0;
        }

        /// <summary>
        /// Whether a harvest brings its protector out: one in <see cref="OneIn"/> from job level
        /// <see cref="FromJobLevel"/>, times today's Almanax.
        /// </summary>
        public static bool Comes(int jobLevel, Random dice)
        {
            if (jobLevel < FromJobLevel) return false;
            long odds = Almanax.WithBonus(Almanax.BonusType.ResourceProtectors, 100);
            return dice.Next(OneIn * 100) < odds;
        }

        /// <summary>The grade, from 0, a job level brings out: the highest whose level it reaches.</summary>
        public static int GradeFor(IReadOnlyList<int> gradeLevels, int jobLevel)
        {
            int grade = 0;
            for (int i = 0; i < gradeLevels.Count; i++)
                if (gradeLevels[i] <= jobLevel) grade = i;
            return grade;
        }

        /// <summary>
        /// After a harvest: maybe its protector, at the job's grade, fighting the gatherer where he
        /// stands. Called with his session's turn held, as a packet's work is.
        /// </summary>
        public static async Task MayComeOutAsync(NetworkStream stream, int resourceItem, int jobLevel)
        {
            if (!Comes(jobLevel, Random.Shared)) return;
            int protector = ProtectorOf(resourceItem);
            var data = protector == 0 ? null : MobSpawnManager.GetMonsterData(protector);
            if (data == null || data.Grades.Count == 0) return;
            if (SessionContext.State.IsInFight) return;

            int grade = GradeFor(data.Grades.Select(g => g.Level).ToList(), jobLevel);
            var group = MobSpawnManager.ComposeOffMap(new[] { (protector, grade) });
            if (group == null || group.Members.Count == 0) return;
            group.Members[0].GradeIndex = grade;
            group.Members[0].Level = data.Grades[grade].Level;

            long mapId = SessionContext.State.MapId;
            Console.WriteLine($"[Jobs] {SessionContext.State.CharacterName}'s harvest of {resourceItem} brings out its " +
                              $"protector {protector} (grade {grade + 1}) on {mapId}.");
            await Handlers.FightHandler.InitiateFightFromMobCollision(stream, group, mapId);
        }

        private static void Load()
        {
            if (_byResource != null) return;
            lock (_lock)
            {
                if (_byResource != null) return;
                var byResource = new Dictionary<int, int>();
                try
                {
                    using var connection = new SqliteConnection(DatabaseManager.WorldConnectionString);
                    connection.Open();

                    // The protectors and the items they drop.
                    var drops = new Dictionary<int, List<int>>();
                    var monsters = connection.CreateCommand();
                    monsters.CommandText = "SELECT Id, Data FROM MonsterTemplates;";
                    using (var reader = monsters.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (reader.IsDBNull(1)) continue;
                            using var doc = JsonDocument.Parse(reader.GetString(1));
                            var root = doc.RootElement;
                            if (!root.TryGetProperty("race", out var race) || !Races.Contains(race.GetInt32())) continue;
                            var items = new List<int>();
                            foreach (var drop in Unwrap(root, "drops"))
                                if (drop.TryGetProperty("objectId", out var item)) items.Add(item.GetInt32());
                            drops[reader.GetInt32(0)] = items;
                        }
                    }

                    // Which of those items is a sack, and of what.
                    var sackOf = new Dictionary<int, int>();
                    foreach (int item in drops.Values.SelectMany(d => d).Distinct())
                    {
                        var template = connection.CreateCommand();
                        template.CommandText = "SELECT Data FROM ItemTemplates WHERE Id = $id;";
                        template.Parameters.AddWithValue("$id", item);
                        if (template.ExecuteScalar() is not string data) continue;
                        using var doc = JsonDocument.Parse(data);
                        foreach (var effect in Unwrap(doc.RootElement, "possibleEffects"))
                        {
                            if (!effect.TryGetProperty("rid", out var rid)) continue;
                            var row = connection.CreateCommand();
                            row.CommandText = "SELECT EffectId, Value FROM ItemEffects WHERE Rid = $rid;";
                            row.Parameters.AddWithValue("$rid", rid.GetInt64());
                            using var effects = row.ExecuteReader();
                            if (effects.Read() && effects.GetInt32(0) == SackHolds && effects.GetInt32(1) > 0)
                                sackOf[item] = effects.GetInt32(1);
                        }
                    }

                    foreach (var (protector, items) in drops.OrderBy(d => d.Key))
                        foreach (int item in items)
                            if (sackOf.TryGetValue(item, out int resource)) byResource.TryAdd(resource, protector);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Jobs] Could not read the resources' protectors: {ex.Message}");
                }
                _byResource = byResource;
            }
        }

        private static IEnumerable<JsonElement> Unwrap(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out var value)) return Array.Empty<JsonElement>();
            if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("Array", out var inner)) value = inner;
            return value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToList() : Array.Empty<JsonElement>();
        }
    }
}
