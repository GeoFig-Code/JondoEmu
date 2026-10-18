using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Jondo.Unity.World.Content;
using Jondo.Unity.World.Fights;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The Santuario's four guardians: one in the middle of each zone, with four companions, in
    /// fights of four players at most (the guides). Beating the four is what opens the castle.
    /// </summary>
    /// <remarks>
    /// WHERE: each in the zone its name gives -- the Vigilante de la Obra, the Guardián del Enclave,
    /// the Defensor de la Reserva, the Centinela de la Corte (the Patio de Efedra) -- on the zone's
    /// central map, the one the hub's portal arrives at (GuildRaidPassages.ArrivalOf). The client's
    /// world places none of them: no group of the Santuario carries a guardian, nor its bosses.
    ///
    /// WHEN: "Après avoir terminé et résolu les 4 énigmes, les gardiens de chacune des zones se
    /// réveillent" (DPLN), all four at once; and the client's data says the same, each guardian's
    /// goal ("Vencer al vigilante de la obra"...) waiting for the four enigmas' (its
    /// requisiteForDisplay, 1 to 4). So none stands at the raid's start: they wake as the last of
    /// what their goals wait for is met (<see cref="GoalMetAsync"/>), each where its zone's centre
    /// has its sleeping statue.
    ///
    /// WHAT IS SEEN, from the maps' own stagings: on each zone's centre, "#SD Spawn_boss" -- the grey
    /// statue ("boss", "statue_gris") gone in a puff, the portal's leaves turning --, and on the hub
    /// "Logo_miniboss_ouvrage" (clos, reserve, cour). INFERRED, the data pairing them so: a guardian
    /// beaten ends its zone's aura and turns its portal back ("#SD Fin aura boss", "Zone finit"),
    /// its logo goes from the hub ("depop_mb_..."), and once the four are beaten the hub's door to
    /// the corridor dissolves ("Dissolve porte") -- the door the corridor's goal opens.
    ///
    /// WITH WHOM: "four monsters, one of each kind" -- the Santuario's Trilipín, Machacamelia,
    /// Muguetégida and Daliana, the castle's colourless ones -- and the Centinela, its four
    /// obelisks besides.
    ///
    /// THE CENTINELA'S COLOUR: in the guides it is the colour the Obra takes when its enigma is
    /// solved. Its obelisks and its spells list a version per colour -- "Check color == 1" to "4",
    /// "Caleidohueso color == 1 / feu" -- and the data picks none: the server does
    /// (<see cref="ChosenCasts"/>), by the raid's colour, drawn at its start and shown by the Obra
    /// once solved (GuildRaidMonochrome). The players read it off the element of its hits too.
    ///
    /// THE VIGILANTE AND THE GUARDIÁN start invulnerable, and what frees them is the client's own
    /// data: scripts written for the fight's scene, a carrier of neither side ("Sce", with "Def"
    /// and "Atq" for the sides), which the guardian itself stands for here
    /// (<see cref="FightInstance.SceneStandIn"/>).
    ///
    ///   The Vigilante carries "Check Mob" (32099). Each kind of companion's death casts its
    ///   "mob == N" and its "mob != N": the first frees it (32108: invulnerability off, 120 % damage
    ///   taken, 110 % for its companions, Check Mob gone), the second ends the fight.
    ///
    ///   The Guardián carries "Check délock" (32090) and lays its eight "Glyphe Indice" (o_hint_1
    ///   to 8). A step on one casts its "o_hint_N &lt; 5" -- one more on the counter of states,
    ///   that glyph gone -- or its "o_hint_N &gt; 4", which ends the fight. The fourth step frees
    ///   it and takes the other glyphs away.
    ///
    /// Which companion and which glyphs are right is what the Enclave's and the Obra's enigmas
    /// showed (DPLN). "mob" is the kind of the statue the Enclave raised
    /// (GuildRaidEnclave.StatueVariable, numbered as the checks number the companions): the first
    /// companion to fall frees the Vigilante if it is of that kind, and ends the fight otherwise.
    /// The Guardián's glyphs, "pour chaque glyphe correspond un objet (placé à côté)": its arena is
    /// the Enclave's centre's twin that has the pedestals' eight objects drawn on it
    /// (<see cref="ArenaOf"/>), a glyph is laid beside each, and "o_hint_N" is below 5 for the
    /// four objects whose maps had the Obra's flowers. Where the data leaves a raid nothing to go
    /// by -- no statue, no objects --, the right branch goes through, as before.
    /// A glyph's check goes to every attacker on the board; it counts once. The wrong branches end
    /// the fight with the client's 3402, lost by the players (FightHandler.EffectEndedTheFightAsync).
    /// </remarks>
    public static class GuildRaidGuardians
    {
        /// <summary>The guardians by the zone they guard, in the raid's zone order.</summary>
        public static readonly IReadOnlyDictionary<int, int> GuardianOfZone = new Dictionary<int, int>
        {
            [1] = 8318,     // Vigilante de la Obra
            [2] = 8317,     // Guardián del Enclave
            [3] = 8319,     // Defensor de la Reserva
            [4] = 8316,     // Centinela de la Corte, the Patio de Efedra's
        };

        /// <summary>One of each of the Santuario's kinds: Trilipín, Machacamelia, Muguetégida, Daliana.</summary>
        public static readonly IReadOnlyList<int> Companions = new[] { 8285, 8286, 8287, 8288 };

        /// <summary>The Centinela's obelisks: escarlata, azulado, ambarino, viridina.</summary>
        public static readonly IReadOnlyList<int> Obelisks = new[] { 8320, 8321, 8322, 8323 };

        public const int Centinela = 8316;
        public const int Vigilante = 8318;
        public const int Guardian = 8317;

        /// <summary>The client's state 56, invulnerable, which the guardians' data puts on them.</summary>
        public const int Invulnerable = 56;

        /// <summary>The Vigilante's "Check Mob", the watcher of its companions' deaths.</summary>
        public const int CheckMob = 32099;

        /// <summary>The Guardián's "Check délock", which frees it at the fourth glyph.</summary>
        public const int CheckUnlock = 32090;

        /// <summary>The Guardián's eight "Glyphe Indice", o_hint_1 to o_hint_8.</summary>
        public static readonly IReadOnlyList<int> GlyphSpells = new[] { 32064, 32069, 32070, 32071, 32072, 32073, 32074, 32075 };

        /// <summary>How far apart, at least, the glyphs are laid when the board has room.</summary>
        public const int GlyphSpacing = 3;

        /// <summary>The guardians' fights take four players at most (the guides).</summary>
        public const int PeopleCap = 4;

        /// <summary>The raid's colour, 1 to 4 as the client's spells number them.</summary>
        public const string ColourVariable = "Santuario_Color";

        // ─── Placing them ───────────────────────────────────────────────────────────────────

        /// <summary>A guardian's group: it, its companions, and the Centinela's obelisks.</summary>
        public static List<int> GroupOf(int guardian)
        {
            var members = new List<int> { guardian };
            members.AddRange(Companions);
            if (guardian == Centinela) members.AddRange(Obelisks);
            return members;
        }

        /// <summary>Puts each guardian with its group on its zone's central map, by its statue. How many were placed.</summary>
        public static int Place(RaidKind kind)
        {
            if (kind == null || kind.Id != Raids.EternalGardens) return 0;
            int placed = 0;
            foreach (var (zone, guardian) in GuardianOfZone)
            {
                if (GuildRaidPassages.ArrivalOf(kind, zone) is not { } arrival) continue;
                var group = MobSpawnManager.SpawnComposed(arrival.MapId, GroupOf(guardian).Select(m => (m, 0)), CellOf(arrival.MapId));
                if (group == null) continue;
                placed++;
                Program.LogDebug($"[Raids] Guardian {guardian} placed on map {arrival.MapId}, cell {group.CellId}.");
            }
            return placed;
        }

        /// <summary>
        /// Where a guardian wakes on its zone's centre: the walkable cell nearest its sleeping statue
        /// (the element the map's data names "boss"), or -1 for the map's own choice.
        /// </summary>
        public static int CellOf(long centre)
        {
            int statue = GuildRaidStagings.ElementsOf(centre).Where(e => e.Name == Statue).Select(e => e.Cell).DefaultIfEmpty(-1).Min();
            return statue < 0 ? -1 : MapManager.GetNearestWalkableCell(centre, statue);
        }

        // ─── Waking them, and each one beaten ───────────────────────────────────────────────

        /// <summary>Whether a raid's guardians are awake: 1 once they woke.</summary>
        public const string AwakeVariable = "Guardians_Awake";

        /// <summary>The stagings and elements, by their names in the client's maps.</summary>
        public const string Statue = "boss";
        public const string Wakes = "#SD Spawn_boss";
        public const string AuraEnds = "#SD Fin aura boss";
        public const string ZoneDone = "Zone finit";
        public const string DoorDissolves = "Dissolve porte";
        public static string HubLogo(int zone) => $"Logo_miniboss_{HubWords[zone]}";
        public static string HubLogoGone(int zone) => $"depop_mb_{HubWords[zone]}";

        /// <summary>How the hub's stagings name the zones: the Obra "ouvrage", the Enclave "clos", the Reserva "reserve", the Patio "cour".</summary>
        private static readonly IReadOnlyDictionary<int, string> HubWords = new Dictionary<int, string>
        {
            [1] = "ouvrage", [2] = "clos", [3] = "reserve", [4] = "cour",
        };

        /// <summary>A guardian's goal in its raid: the one naming it ("Vencer al vigilante de la obra").</summary>
        public static GuildRaidCatalogue.Goal GoalOf(int raidId, int guardian)
            => GuildRaidCatalogue.GoalsOf(raidId).FirstOrDefault(g => g.Monsters?.Contains(guardian) == true);

        /// <summary>What the guardians' goals wait for, in the client's data: the four enigmas'.</summary>
        public static List<int> WaitedFor(int raidId)
            => GuardianOfZone.Values.Select(g => GoalOf(raidId, g)).Where(g => g?.RequisiteForDisplay != null)
                             .SelectMany(g => g.RequisiteForDisplay.Where(r => r != g.Id)).Distinct().OrderBy(r => r).ToList();

        /// <summary>Whether a raid's goal is met: at its value, one at least.</summary>
        private static bool Met(RaidInstance raid, int goalId)
            => GuildRaidCatalogue.GoalsOf(raid.RaidId).FirstOrDefault(g => g.Id == goalId) is { } goal
               && raid.GoalAt(goalId) >= Math.Max(1, goal.Value);

        /// <summary>Whether a raid's guardians are due to wake: asleep, and everything their goals wait for met.</summary>
        public static bool Due(RaidInstance raid)
        {
            if (raid == null || raid.RaidId != Raids.EternalGardens || raid.Get(AwakeVariable) != 0) return false;
            var waited = WaitedFor(raid.RaidId);
            return waited.Count > 0 && waited.All(id => Met(raid, id));
        }

        /// <summary>
        /// A goal of a raid just met: the guardians wake once the last of what theirs wait for is; a
        /// guardian's own is that guardian beaten.
        /// </summary>
        public static async Task GoalMetAsync(RaidInstance raid, int goalId)
        {
            if (raid == null || raid.RaidId != Raids.EternalGardens) return;
            if (Due(raid)) await WakeAsync(raid);
            foreach (var (zone, guardian) in GuardianOfZone)
            {
                if (GoalOf(raid.RaidId, guardian)?.Id == goalId) await BeatenAsync(raid, zone);
            }
        }

        /// <summary>
        /// The four guardians wake: each zone's statue goes in a puff and its guardian stands there with
        /// its group, seen at once by whoever is on the map; the hub shows their four logos.
        /// </summary>
        public static async Task WakeAsync(RaidInstance raid)
        {
            raid.Set(AwakeVariable, 1);
            var kind = Raids.Of(raid.RaidId);
            long hub = GuildRaidPassages.HubOf(kind)?.MapId ?? 0;
            foreach (var zone in GuardianOfZone.Keys)
            {
                if (GuildRaidPassages.ArrivalOf(kind, zone) is not { } centre) continue;
                await GuildRaidStagings.PlayAsync(raid, centre.MapId, Wakes, keep: true);
                if (hub != 0) await GuildRaidStagings.PlayAsync(raid, hub, HubLogo(zone), keep: true);
            }
            int placed = Place(kind);
            foreach (var zone in GuardianOfZone.Keys)
            {
                if (GuildRaidPassages.ArrivalOf(kind, zone) is { } centre) await MapAdmin.RefreshAsync(centre.MapId);
            }
            Console.WriteLine($"[Raid] {raid.Uuid}: the Santuario's guardians wake ({placed} placed).");
        }

        /// <summary>
        /// A zone's guardian beaten: its aura ends and its portal turns back, its logo leaves the hub;
        /// the fourth dissolves the hub's door to the corridor.
        /// </summary>
        public static async Task BeatenAsync(RaidInstance raid, int zone)
        {
            var kind = Raids.Of(raid.RaidId);
            long hub = GuildRaidPassages.HubOf(kind)?.MapId ?? 0;
            if (GuildRaidPassages.ArrivalOf(kind, zone) is { } centre)
            {
                await GuildRaidStagings.PlayAsync(raid, centre.MapId, AuraEnds, keep: true);
                await GuildRaidStagings.PlayAsync(raid, centre.MapId, ZoneDone, keep: true);
            }
            if (hub == 0) return;
            GuildRaidStagings.Drop(raid, hub, GuildRaidStagings.IdOf(hub, HubLogo(zone)));
            await GuildRaidStagings.PlayAsync(raid, hub, HubLogoGone(zone), keep: false);
            if (GuardianOfZone.Values.All(g => GoalOf(raid.RaidId, g) is { } goal && Met(raid, goal.Id)))
            {
                await GuildRaidStagings.PlayAsync(raid, hub, DoorDissolves, keep: true);
                Console.WriteLine($"[Raid] {raid.Uuid}: the four guardians beaten, the corridor's door dissolves.");
            }
        }

        /// <summary>
        /// The arena of a fight against the Guardián: the map of its zone with the pedestals' eight
        /// objects drawn on it, one beside each of its glyphs. 0 for any other group, or none such.
        /// </summary>
        public static long ArenaOf(MobSpawnManager.MobGroup group, long mapId)
        {
            if (group?.Members.Any(m => m.Monster.Id == Guardian) != true) return 0;
            int objects = GuildRaidMonochrome.WorkOf(Raids.Of(Raids.EternalGardens))?.Objects.Count ?? 0;
            if (objects == 0) return 0;
            return DatabaseManager.MapsOfSubArea(GuildRaidManager.SubAreaOf(mapId)).OrderBy(m => m)
                                  .FirstOrDefault(m => GuildRaidStagings.ObjectsOf(m).Select(o => o.Name).Distinct().Count() >= objects);
        }

        /// <summary>Draws the raid's colour, which the Centinela's obelisks and spells follow.</summary>
        public static int DrawColour(RaidInstance raid, Random dice)
        {
            int colour = dice.Next(1, 5);
            raid.Set(ColourVariable, colour);
            return colour;
        }

        // ─── In the fight ───────────────────────────────────────────────────────────────────

        public static bool IsGuardianFight(FightInstance fight)
            => fight.Rojo.Any(m => m.IsMonster && GuardianOfZone.Values.Contains(m.MonsterId));

        /// <summary>
        /// A fight just built: a guardian's takes four players a side, and the Vigilante and the
        /// Guardián stand for its scene and carry their checks, cast with the attitudes before the
        /// first turn.
        /// </summary>
        public static void OnFightCreated(FightInstance fight)
        {
            if (!IsGuardianFight(fight)) return;
            fight.PeopleCap = PeopleCap;
            foreach (var monster in fight.Rojo.Where(m => m.IsMonster).ToList())
            {
                int check = monster.MonsterId switch { Vigilante => CheckMob, Guardian => CheckUnlock, _ => 0 };
                if (check == 0) continue;
                monster.Buffs.PonerActitud(check);
                fight.SceneStandIn = monster;
            }
        }

        /// <summary>The cells the Guardián's glyphs go on: free cells of the board, spread apart.</summary>
        public static List<int> GlyphCells(IEnumerable<int> walkable, ISet<int> taken, int count, Random dice)
        {
            var free = (walkable ?? Enumerable.Empty<int>()).Where(c => !taken.Contains(c)).OrderBy(c => c).ToList();
            var chosen = new List<int>();
            while (chosen.Count < count && free.Count > 0)
            {
                var apart = free.Where(c => chosen.All(o => Jondo.Unity.World.Maps.MapGeometry.Distance(o, c) >= GlyphSpacing)).ToList();
                var pool = apart.Count > 0 ? apart : free;
                int cell = pool[dice.Next(pool.Count)];
                chosen.Add(cell);
                free.Remove(cell);
            }
            return chosen;
        }

        /// <summary>
        /// The fight has begun: the Guardián lays its eight glyphs -- each beside one of the arena's
        /// objects, the object o_hint_N stands for, or spread over the board where it has none.
        /// </summary>
        internal static async Task OnFightStartedAsync(NetworkStream stream, FightInstance fight)
        {
            var guardian = fight?.Rojo.FirstOrDefault(m => m.IsAlive && m.IsMonster && m.MonsterId == Guardian);
            if (guardian == null) return;
            var taken = new HashSet<int>(fight.Azul.Concat(fight.Rojo).Where(f => f.IsAlive).Select(f => f.CellId));
            var walkable = MapManager.GetFightWalkable(fight.MapId);
            var objects = GuildRaidStagings.ObjectsOf(fight.MapId).GroupBy(o => o.Name).Select(g => g.First())
                                           .OrderBy(o => o.Name, StringComparer.Ordinal).Take(GlyphSpells.Count).ToList();
            List<int> cells;
            if (objects.Count == GlyphSpells.Count)
            {
                cells = BesideCells(walkable, taken, objects.Select(o => o.Cell).ToList());
                _hints[fight.FightId] = objects.Select(o => o.Name).ToList();
            }
            else cells = GlyphCells(walkable, taken, GlyphSpells.Count, Random.Shared);
            for (int i = 0; i < cells.Count; i++)
                await Handlers.FightHandler.CastAtAsync(stream, fight, guardian, GlyphSpells[i], 1, cells[i]);
            Program.LogDebug($"[Raids] The Guardián lays its {cells.Count} glyphs on {string.Join(", ", cells)}" +
                             (objects.Count == GlyphSpells.Count ? $", beside {string.Join(", ", objects.Select(o => o.Name))}." : "."));
        }

        /// <summary>The object each of a fight's glyphs stands beside, by o_hint_N - 1.</summary>
        private static readonly ConcurrentDictionary<long, IReadOnlyList<string>> _hints = new();

        /// <summary>
        /// A free cell beside each object, in order: the walkable cell nearest it that no one stands
        /// on and no glyph before took.
        /// </summary>
        public static List<int> BesideCells(IEnumerable<int> walkable, ISet<int> taken, IReadOnlyList<int> objects)
        {
            var free = (walkable ?? Enumerable.Empty<int>()).Where(c => !taken.Contains(c)).OrderBy(c => c).ToList();
            var chosen = new List<int>();
            foreach (int cell in objects)
            {
                var near = free.Where(c => !chosen.Contains(c))
                               .OrderBy(c => Jondo.Unity.World.Maps.MapGeometry.Distance(c, cell)).ThenBy(c => c)
                               .Cast<int?>().FirstOrDefault();
                if (near is int beside) chosen.Add(beside);
            }
            return chosen;
        }

        /// <summary>
        /// The verdict on a check of the Vigilante's or the Guardián's, by its admin name: "mob == N"
        /// and "o_hint_N &lt; 5" go through, "mob != N" and "o_hint_N &gt; 4" do not; null for any
        /// other spell.
        /// </summary>
        public static bool? CheckVerdict(int spell)
        {
            string admin = AdminNameOf(spell);
            if (Regex.IsMatch(admin, @"^mob\s*==\s*\d") || Regex.IsMatch(admin, @"^o_hint_\d+\s*<\s*5")) return true;
            if (Regex.IsMatch(admin, @"^mob\s*!=\s*\d") || Regex.IsMatch(admin, @"^o_hint_\d+\s*>\s*4")) return false;
            return null;
        }

        private static readonly ConcurrentDictionary<(long Fight, int Check), byte> _counted = new();

        /// <summary>
        /// Whether a check's cast goes through: the one whose condition holds for the fight's raid --
        /// "mob == 2" with the Enclave's statue of kind 2, "o_hint_3 &lt; 5" with the third glyph's
        /// object a flower's --, once a fight: a glyph's goes to every attacker on the board and is
        /// one step all the same. With nothing to go by, the freeing branch goes through.
        /// </summary>
        public static bool Passes(FightInstance fight, int check)
        {
            bool? branch = CheckVerdict(check);
            if (branch == null) return false;
            if (!(Holds(fight, check) ?? branch.Value)) return false;
            return fight == null || _counted.TryAdd((fight.FightId, check), 0);
        }

        /// <summary>
        /// Whether a check's condition holds for the fight's raid, by its admin name; null when the
        /// raid has nothing to say -- no statue raised, no objects beside the glyphs, no flowers.
        /// </summary>
        public static bool? Holds(FightInstance fight, int check) => Holds(RaidOf(fight), fight?.FightId ?? 0, check);

        /// <summary>The same, for a raid and a fight by its id (and for the tests).</summary>
        internal static bool? Holds(RaidInstance raid, long fightId, int check)
        {
            if (raid == null) return null;
            string admin = AdminNameOf(check);
            var mob = Regex.Match(admin, @"^mob\s*(==|!=)\s*(\d+)");
            if (mob.Success)
            {
                int statue = (int)raid.Get(GuildRaidEnclave.StatueVariable);
                if (statue == 0) return null;
                bool same = statue == int.Parse(mob.Groups[2].Value);
                return mob.Groups[1].Value == "==" ? same : !same;
            }
            var hint = Regex.Match(admin, @"^o_hint_(\d+)\s*(<|>)");
            if (hint.Success)
            {
                int n = int.Parse(hint.Groups[1].Value);
                var flowers = GuildRaidMonochrome.FlowerObjects(raid);
                if (!_hints.TryGetValue(fightId, out var objects) || n < 1 || n > objects.Count || flowers.Count == 0)
                    return null;
                bool right = flowers.Contains(objects[n - 1]);
                return hint.Groups[2].Value == "<" ? right : !right;
            }
            return null;
        }

        /// <summary>The glyphs of a fight laid as given: o_hint_N beside objects[N - 1] (for the tests).</summary>
        internal static void SetHints(long fightId, IReadOnlyList<string> objects) => _hints[fightId] = objects;

        /// <summary>A fight is over: its counted checks and its glyphs' objects go.</summary>
        public static void Forget(long fightId)
        {
            foreach (var key in _counted.Keys.Where(k => k.Fight == fightId).ToList()) _counted.TryRemove(key, out _);
            _hints.TryRemove(fightId, out _);
        }

        private static readonly ConcurrentDictionary<int, string> _adminNames = new();

        /// <summary>
        /// The colour a spell is the version of, by its admin name -- "Check color == 1",
        /// "color == 3 / eau" -- or 0 when it is none.
        /// </summary>
        public static int ColourOf(int spell)
        {
            var match = Regex.Match(AdminNameOf(spell), @"color\s*==\s*(\d)");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        /// <summary>A spell's admin name in the client's data, read once.</summary>
        internal static string AdminNameOf(int spell) => _adminNames.GetOrAdd(spell, ReadAdminName);

        private static IReadOnlyDictionary<int, int>? _companionByMob;

        /// <summary>
        /// The companions by the number the Vigilante's "Check Mob" gives each kind: the death of each
        /// casts a grade of it whose "mob == N" names its N -- 1 Machacamelia, 2 Trilipán, 3 Daliana,
        /// 4 Muguetégida in the client's data. The Enclave's statues are numbered alike ("Pop statue N").
        /// </summary>
        public static IReadOnlyDictionary<int, int> CompanionByMob()
        {
            if (_companionByMob != null) return _companionByMob;
            var byMob = new Dictionary<int, int>();
            foreach (var death in SpellEffects.De(CheckMob, 1))
            {
                if (death.EffectId != EffectEngine.EfectoQueLanzaHechizo || death.DiceNum != CheckMob) continue;
                var companion = Regex.Match(death.Triggers + "," + death.TargetMask, @"\bF(\d+)");
                if (!companion.Success) continue;
                foreach (var check in SpellEffects.De(CheckMob, death.DiceSide))
                {
                    var mob = Regex.Match(AdminNameOf(check.DiceNum), @"^mob\s*==\s*(\d+)");
                    if (check.EffectId == EffectEngine.EfectoQueLanzaHechizo && mob.Success)
                        byMob[int.Parse(mob.Groups[1].Value)] = int.Parse(companion.Groups[1].Value);
                }
            }
            if (byMob.Count > 0) _companionByMob = byMob;
            return byMob;
        }

        private static string ReadAdminName(int spell)
        {
            try
            {
                using var connection = new SqliteConnection(DatabaseManager.WorldConnectionString);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT Data FROM SpellTemplates WHERE Id = $id;";
                command.Parameters.AddWithValue("$id", spell);
                if (command.ExecuteScalar() is not string data) return "";
                using var doc = JsonDocument.Parse(data);
                return doc.RootElement.TryGetProperty("adminName", out var a) ? a.GetString() ?? "" : "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>The colour of the raid a fight is fought in, or 0.</summary>
        public static int ColourOf(FightInstance fight)
        {
            var raid = RaidOf(fight);
            return raid == null ? 0 : (int)raid.Get(ColourVariable);
        }

        /// <summary>The raid a fight is fought in, by its first player, or null.</summary>
        private static RaidInstance RaidOf(FightInstance fight)
        {
            var player = fight?.Azul.Concat(fight.Rojo).FirstOrDefault(f => !f.IsMonster && !f.EsInvocado);
            return player == null ? null : GuildRaidManager.RaidOf(player.Id);
        }
    }
}
