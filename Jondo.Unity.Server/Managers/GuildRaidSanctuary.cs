using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Jondo.Unity.Server.Network;
using Jondo.Unity.World.Content;
using Jondo.Unity.World.Fights;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The Santuario's castle: the corridor of ten rooms from the hub to the tower, the tower with
    /// the Reina below and the Princesa above, and the manuscript's five pages.
    /// </summary>
    /// <remarks>
    /// THE CASTLE is the raid's last zone. Its maps are walked from the hub through its doors,
    /// arches and edges (GuildRaidPassages.CastleOf, MapScrolls): the ten rooms between the hub
    /// and the tower's foot are the corridor, and the tower is its foot and the two maps its stairs
    /// lead to. Nothing here names a map: the guide's [16,16] to [17,10] are what the walk finds.
    ///
    /// THE WAY IN: "Ils ne vous laisseront pas emprunter le corridor du château tant que vous ne les
    /// aurez pas tous vaincus" (the guides, of the four guardians) -- the corridor's goal waits for
    /// the four guardians' in the client's data -- and "Pour que cette porte s'ouvre, il va falloir
    /// éliminer tous les monstres présents dans le Corridor" -- the bosses' goals wait for the
    /// corridor's. So a corridor room is closed until the goals its goal waits for are met, and the
    /// tower until those the bosses' wait for are (<see cref="ClosedBy"/>).
    ///
    /// THE CORRIDOR: "La zone est composée de 10 salles et dans chaque salle se trouvent 6 monstres
    /// en solo", the castle's four kinds, sixty in all -- its goal's value. "Les combats peuvent
    /// uniquement être fait en solo" and, as they start, the monster gains "30% Vitalité, 20%
    /// Critique, 100 Dommages Critiques, 2PA, 1PM, 50 Tacle et 50 Fuite, 2PO, 300 Puissance" and "un
    /// bouclier équivalent à 100% de leur niveau à chaque fin de tour": exactly the client's spell
    /// 32772, "Protectores del Pasillo", which the monster holds as an attitude, standing for the
    /// scene its mask names ("Def,A"). "Vous êtes entièrement soigné à la fin de chaque combat",
    /// as every fight won is here.
    ///
    /// THE TOWER: "la Reine Écarlate qui se trouve dans les cachots (descendre les escaliers) ainsi
    /// que la Princesse Maudite qui se trouve en haut de la tour (montez les escaliers)", each
    /// fought by "8 joueurs maximum". Their auras are their grades' starting spells.
    ///
    /// THE MANUSCRIPT: one page on each zone ("Première page du manuscrit : Ouvrage Monochrome
    /// [22,9]" ... "Cinquième page du manuscrit : Corridor du Château [16,12]"), the client's
    /// element 401479 of type 458 "Manuscrito", one on each of the raid's zones and in that order:
    /// the zone's number is the page's. The name of its skill is the client's "Arrancar una
    /// página" (SkillNamesDataRoot 391).
    /// </remarks>
    public static class GuildRaidSanctuary
    {
        /// <summary>The Reina Escarlata, in the dungeons, and the Princesa Maldita, at the top.</summary>
        public const int Reina = 8278;
        public const int Princesa = 8279;

        /// <summary>"Protectores del Pasillo", what the corridor's monsters gain as the fight starts.</summary>
        public const int CorridorProtectors = 32772;

        /// <summary>The corridor is fought alone; the bosses by eight at most (the guides).</summary>
        public const int CorridorPeopleCap = 1;
        public const int TowerPeopleCap = 8;

        /// <summary>The manuscript: its graphic, the client's type 458, "Utilizar" and "Arrancar una página".</summary>
        public const int ManuscriptGfx = 401479;
        public const int ManuscriptType = 458;
        public const int ManuscriptSkill = 184;
        public const int TearPageName = 391;

        /// <summary>"Primera página del manuscrito"; the second to the fifth follow it.</summary>
        public const int FirstPage = 34304;

        /// <summary>How far apart, at least, a room's monsters stand when it has room for it.</summary>
        public const int MonsterSpacing = 4;

        /// <summary>The castle's maps: its hub, its corridor's rooms in the order they are walked, and its tower.</summary>
        public sealed record Castle(long Hub, IReadOnlyList<long> Corridor, long Foot, long Dungeon, long Top)
        {
            public bool InCorridor(long mapId) => Corridor.Contains(mapId);
            public bool InTower(long mapId) => mapId != 0 && (mapId == Foot || mapId == Dungeon || mapId == Top);
        }

        private static readonly ConcurrentDictionary<int, Castle> _castles = new();

        /// <summary>A raid's castle, or null for one without a hub and a tower.</summary>
        public static Castle CastleOf(RaidKind kind)
        {
            if (kind == null) return null;
            if (_castles.TryGetValue(kind.Id, out var known)) return known;
            var castle = Walk(kind);
            if (castle != null) _castles[kind.Id] = castle;
            return castle;
        }

        /// <summary>The castle walked from its hub, by its ways and its edges, as far as the tower.</summary>
        private static Castle Walk(RaidKind kind)
        {
            if (kind.Floors.Count == 0 || GuildRaidPassages.HubOf(kind) is not { } hub
                || GuildRaidPassages.TowerOf(kind) is not { } tower) return null;
            var maps = DatabaseManager.MapsOfSubArea(kind.Floors[^1]).ToHashSet();
            if (!maps.Contains(hub.MapId)) return null;

            var ways = GuildRaidPassages.CastleOf(kind).ToLookup(r => r.SourceMapId, r => r.DestinationMapId);
            var seen = new HashSet<long> { hub.MapId };
            var order = new List<long>();
            var next = new Queue<long>();
            next.Enqueue(hub.MapId);
            while (next.Count > 0)
            {
                long map = next.Dequeue();
                order.Add(map);
                if (map == tower.Foot) continue;
                foreach (long other in ways[map].Concat(EdgesOf(map)).OrderBy(m => m))
                {
                    if (!maps.Contains(other) || !seen.Add(other)) continue;
                    next.Enqueue(other);
                }
            }

            // A walk that never reached the tower is no castle: its rooms would not be the corridor.
            if (!order.Contains(tower.Foot)) return null;
            var corridor = order.Where(m => m != hub.MapId && m != tower.Foot && m != tower.Dungeon && m != tower.Top).ToList();
            return new Castle(hub.MapId, corridor, tower.Foot, tower.Dungeon, tower.Top);
        }

        /// <summary>The maps a map's edges lead to, from the world's MapScrolls.</summary>
        private static List<long> EdgesOf(long mapId)
        {
            var edges = new List<long>();
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(DatabaseManager.WorldConnectionString);
            connection.Open();
            var query = connection.CreateCommand();
            query.CommandText = "SELECT TopMapId, RightMapId, BottomMapId, LeftMapId FROM MapScrolls WHERE MapId = $m;";
            query.Parameters.AddWithValue("$m", mapId);
            using var reader = query.ExecuteReader();
            while (reader.Read())
                for (int i = 0; i < 4; i++)
                    if (!reader.IsDBNull(i) && reader.GetInt64(i) > 0) edges.Add(reader.GetInt64(i));
            return edges;
        }

        // ─── The way in ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The goal still to be met before this map lets a raid member in: for a corridor room, the
        /// first of those the corridor's goal waits for; for the tower, of those the bosses' wait
        /// for. Null when nothing keeps him out.
        /// </summary>
        public static GuildRaidCatalogue.Goal ClosedBy(RaidInstance raid, long mapId)
        {
            if (raid == null || CastleOf(Raids.Of(raid.RaidId)) is not { } castle) return null;
            var goals = GuildRaidCatalogue.GoalsOf(raid.RaidId);
            IEnumerable<int> waited = castle.InCorridor(mapId) ? RequisitesOf(goals, GuildRaidManager.CorridorGoal)
                                    : castle.InTower(mapId) ? GuildRaidManager.FinalGoals(raid.RaidId).SelectMany(g => RequisitesOf(goals, g))
                                    : Enumerable.Empty<int>();
            return waited.Distinct().OrderBy(id => id)
                         .Select(id => goals.FirstOrDefault(g => g.Id == id))
                         .FirstOrDefault(g => g != null && raid.GoalAt(g.Id) < Math.Max(1, g.Value));
        }

        private static IEnumerable<int> RequisitesOf(IReadOnlyList<GuildRaidCatalogue.Goal> goals, int goalId)
            => goals.FirstOrDefault(g => g.Id == goalId)?.RequisiteForDisplay?.Where(r => r != goalId) ?? Enumerable.Empty<int>();

        // ─── Peopling it ────────────────────────────────────────────────────────────────────

        /// <summary>How many monsters stand in each corridor room: its goal's value shared out between them.</summary>
        public static int MonstersPerRoom(RaidKind kind)
        {
            var castle = CastleOf(kind);
            var goal = kind == null ? null : GuildRaidCatalogue.GoalsOf(kind.Id).FirstOrDefault(g => g.Id == GuildRaidManager.CorridorGoal);
            return castle == null || castle.Corridor.Count == 0 || goal == null ? 0 : Math.Max(1, goal.Value / castle.Corridor.Count);
        }

        /// <summary>
        /// The castle as a raid starts: the world's groups gone from all its maps, each corridor room
        /// with its monsters alone, one of the castle's kinds each, and the bosses in the tower.
        /// How many monsters were placed.
        /// </summary>
        public static int Populate(RaidKind kind, Random dice)
        {
            if (CastleOf(kind) is not { } castle) return 0;
            foreach (long map in DatabaseManager.MapsOfSubArea(kind.Floors[^1])) MobSpawnManager.ClearMap(map);

            int perRoom = MonstersPerRoom(kind), placed = 0;
            foreach (long room in castle.Corridor)
            {
                foreach (int cell in SpreadCells(CellsFor(room, perRoom), perRoom, dice))
                {
                    int monster = GuildRaidGuardians.Companions[dice.Next(GuildRaidGuardians.Companions.Count)];
                    int grades = MobSpawnManager.GetMonsterData(monster)?.Grades.Count ?? 1;
                    if (MobSpawnManager.SpawnComposed(room, new[] { (monster, dice.Next(Math.Max(1, grades))) }, cell) != null) placed++;
                }
            }

            foreach (var (map, boss) in new[] { (castle.Dungeon, Reina), (castle.Top, Princesa) })
            {
                if (map == 0) continue;
                int cell = MapManager.GetNearestWalkableCell(map, Handlers.TeleportHandler.MapCentre);
                if (MobSpawnManager.SpawnComposed(map, new[] { (boss, 0) }, cell > 0 ? cell : -1) != null) placed++;
            }
            Program.LogDebug($"[Raids] The castle peopled: {placed} monsters in {castle.Corridor.Count} rooms and the tower.");
            return placed;
        }

        /// <summary>
        /// Where a room's monsters may stand: its cells well inside, as the world's groups do, and
        /// when they are too few -- [16,15] has five -- its other walkable ones, away from its edges
        /// and from what is clickable.
        /// </summary>
        public static List<int> CellsFor(long room, int count)
        {
            var inner = MobSpawnManager.GetInnerWalkableCells(room);
            if (inner.Count >= count || !MapManager.WalkableCells.TryGetValue(room, out var walkable)) return inner;
            var clickable = Interactives.ElementsOf(room).Select(e => e.Cell).ToHashSet();
            return inner.Concat(walkable.Where(c => !clickable.Contains(c) && c / 14 is >= EdgeRows and < 40 - EdgeRows
                                                     && c % 14 is >= 1 and <= 12).OrderBy(c => c))
                        .Distinct().ToList();
        }

        /// <summary>The rows at a map's top and bottom a room's monsters keep off, where its edges are.</summary>
        private const int EdgeRows = 3;

        /// <summary>Cells of a room for its monsters: drawn among its free ones, apart from each other when there is room.</summary>
        public static List<int> SpreadCells(IEnumerable<int> cells, int count, Random dice)
        {
            var free = (cells ?? Enumerable.Empty<int>()).Distinct().OrderBy(c => c).ToList();
            var chosen = new List<int>();
            while (chosen.Count < count && free.Count > 0)
            {
                var apart = free.Where(c => chosen.All(o => Jondo.Unity.World.Maps.MapGeometry.Distance(o, c) >= MonsterSpacing)).ToList();
                var pool = apart.Count > 0 ? apart : free;
                int cell = pool[dice.Next(pool.Count)];
                chosen.Add(cell);
                free.Remove(cell);
            }
            return chosen;
        }

        // ─── In the fight ───────────────────────────────────────────────────────────────────

        /// <summary>Whether a fight is fought from one of a raid castle's corridor rooms.</summary>
        public static bool IsCorridorFight(FightInstance fight) => fight != null && CastleAt(fight.RoleplayMapId)?.InCorridor(fight.RoleplayMapId) == true;

        /// <summary>The castle a map is in, or null.</summary>
        public static Castle CastleAt(long mapId)
        {
            var kind = Raids.BySubArea(GuildRaidManager.SubAreaOf(mapId));
            return kind == null || kind.Floors.Count == 0 || kind.FloorOf(GuildRaidManager.SubAreaOf(mapId)) != kind.Floors.Count
                ? null : CastleOf(kind);
        }

        /// <summary>
        /// A fight just built: in the corridor it is fought alone, and its monster stands for the
        /// scene and holds "Protectores del Pasillo", cast with the attitudes before the first
        /// turn; in the tower, by eight at most.
        /// </summary>
        public static void OnFightCreated(FightInstance fight)
        {
            if (fight == null || CastleAt(fight.RoleplayMapId) is not { } castle) return;
            if (castle.InTower(fight.RoleplayMapId))
            {
                fight.PeopleCap = TowerPeopleCap;
                return;
            }
            if (!castle.InCorridor(fight.RoleplayMapId)) return;

            fight.PeopleCap = CorridorPeopleCap;
            foreach (var monster in fight.Rojo.Where(m => m.IsMonster && !m.EsInvocado))
            {
                monster.Buffs.PonerActitud(CorridorProtectors);
                fight.SceneStandIn ??= monster;
            }
        }

        // ─── The manuscript ─────────────────────────────────────────────────────────────────

        /// <summary>The manuscript's elements on a raid's zones, with the page each gives.</summary>
        public static IEnumerable<(long MapId, Interactives.Element Element, int Page)> Manuscripts(RaidKind kind)
        {
            if (kind == null) yield break;
            for (int zone = 1; zone <= kind.Floors.Count; zone++)
            foreach (long map in DatabaseManager.MapsOfSubArea(kind.Floors[zone - 1]).OrderBy(m => m))
            foreach (var element in Interactives.ElementsOf(map).Where(e => e.Gfx == ManuscriptGfx))
                yield return (map, element, FirstPage + zone - 1);
        }

        /// <summary>
        /// A page torn from the manuscript: the raid member gets the page of the zone he is in,
        /// unless he holds it already. False when this is no manuscript of a raid he is in.
        /// </summary>
        public static async Task<bool> TearAsync(NetworkStream stream, long characterId, long mapId, int elementId)
        {
            var raid = GuildRaidManager.RaidOf(characterId);
            var kind = raid == null ? null : Raids.Of(raid.RaidId);
            var manuscript = Manuscripts(kind).Where(m => m.MapId == mapId && m.Element.Id == elementId).ToList();
            if (manuscript.Count == 0) return false;

            int page = manuscript[0].Page;
            if (Equipment.HowMany(page) > 0)
            {
                if (stream != null)
                    await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                        ConnectionProtocol.Push(Jondo.Unity.Protocol.Op.Lqn,
                            ConnectionProtocol.BuildNotice(Handlers.CommandTexts.Get("raid.page.had"))));
                return true;
            }
            if (stream != null) await Equipment.GiveAsync(stream, page, 1);
            Program.LogDebug($"[Raids] {characterId} tears page {page} from the manuscript on map {mapId}.");
            return true;
        }
    }
}
