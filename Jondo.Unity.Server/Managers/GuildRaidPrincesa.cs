using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Jondo.Unity.World.Fights;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The Princesa Maldita's fight at the top of the Santuario's tower: her Flor Maldita, her Aura's
    /// statues, her Floraison.
    /// </summary>
    /// <remarks>
    /// THE GUIDE (DPLN): "Lorsque le combat commence, une Fleur maudite apparaît au milieu de la map.
    /// Elle possède une hitbox de 4 cases ... vous pouvez taper sur n'importe laquelle des 4 cases et
    /// la fleur subira des dégâts. Les sorts de zone n'infligent qu'une seule fois les dégâts." "Au
    /// début du tour, tous les personnages en ligne de vue de la Princesse Maudite sont transformés en
    /// Statue Maudite ... Cet effet ne fonctionne qu'à partir du tour 2." "Les invocations ne sont pas
    /// transformées en statue, vous pouvez donc les utiliser pour bloquer sa ligne de vue."
    ///
    /// THE CLIENT'S DATA: her starting spell (32003) carries the Aura (32010: at each turn start the
    /// statues of the turn before die, then whoever is in her sight -- its zone's
    /// onlyAffectIfInSightLine -- is turned to one, passing his turns until a melee blow frees him),
    /// her Floraison (32012, a state a damage spell up to ten), her threshold and phase II. The Flor's
    /// starting spell (32004) puts its body's three other cells through 32006 ("createHitbox", fixed
    /// cells 355, 369, 370): nothing in the data brings the Flor in, nor reads those cells.
    ///
    /// INFERRED: the Flor comes in as her summon at the board's start, on the cell that closes its
    /// body's square (383).
    ///
    /// THE FLOR'S CAMP: at its threshold (32020) it heals in full and goes over (32021 grade 2: its
    /// "6664", invulnerable, 3107 die 0), and two rounds on comes back (grade 3, by the 792's delay
    /// 2: "6665", vulnerable, 3107 die 1, its threshold put again) -- all the data's. Its 3107 moves
    /// it between the sides for real here (<see cref="ChangesSideForReal"/>): its turn's end heals
    /// and boosts "its allies" (32019 on "g"), the players while it is theirs.
    ///
    /// PHASE II: her threshold's script (32014) heals her, makes her invulnerable, plays twice a round
    /// (4059) and casts 2194 on her scene, where the server brings in the Volonté de la Reine. The
    /// Flor, on her side, lays at its turn a purple glyph for one turn on the board's middle ring;
    /// its stepper is given "Encantamiento Floral" (3405), whose second cast on her takes her
    /// invulnerability (its own states, 6666 then 56 off) and which takes itself away (3406).
    /// </remarks>
    public static class GuildRaidPrincesa
    {
        public const int Princesa = GuildRaidSanctuary.Princesa;

        /// <summary>The Flor Maldita, and the spell whose fixed cells are its body's others.</summary>
        public const int Flor = 8282;
        public const int FlorBody = 32006;

        /// <summary>Her Aura's "Transformation en pierre": her starting spell casts it a round late (its 792's delay 1), "à partir du tour 2".</summary>
        public const int AuraStatues = 32010;

        /// <summary>The Flor's body's other cells, as its "createHitbox" names them.</summary>
        public static IReadOnlyList<int> FlorBodyCells()
            => SpellEffects.De(FlorBody, 1).SelectMany(e => e.CeldasFijas).Distinct().ToList();

        /// <summary>
        /// Where the Flor stands: the walkable cell closing its body's square -- the one nearest the
        /// other three, none of them.
        /// </summary>
        public static int FlorCell(long arena)
        {
            var body = FlorBodyCells();
            if (body.Count == 0) return -1;
            var walkable = MapManager.GetFightWalkable(arena);
            var around = body.SelectMany(Jondo.Unity.World.Maps.MapGeometry.GetNeighbors).Distinct()
                             .Where(c => !body.Contains(c) && (walkable == null || walkable.Count == 0 || walkable.Contains(c)));
            return around.OrderBy(c => body.Sum(b => Jondo.Unity.World.Maps.MapGeometry.Distance(b, c))).ThenBy(c => c)
                         .DefaultIfEmpty(-1).First();
        }

        private static Fighter PrincesaOf(FightInstance fight)
            => fight?.Rojo.FirstOrDefault(m => m.IsAlive && m.IsMonster && !m.EsInvocado && m.MonsterId == Princesa);

        public static bool IsHers(FightInstance fight) => PrincesaOf(fight) != null;

        /// <summary>A fight just built: her scripts are written for her fight's scene ("Sce"), which she stands for.</summary>
        public static void OnFightCreated(FightInstance fight)
        {
            var princesa = PrincesaOf(fight);
            if (princesa != null) fight.SceneStandIn = princesa;
        }

        private static readonly ConcurrentDictionary<long, byte> _started = new();

        /// <summary>The board starts: the Flor Maldita in the middle, its body on its square's four cells.</summary>
        public static async Task OnFightStartedAsync(NetworkStream stream, FightInstance fight)
        {
            var princesa = PrincesaOf(fight);
            if (princesa == null || !_started.TryAdd(fight.FightId, 0)) return;
            int cell = FlorCell(fight.MapId);
            if (cell < 0) return;
            var flor = await Handlers.FightHandler.SummonAsync(stream, fight, princesa, Flor, 1, cell);
            if (flor == null) return;
            flor.ExtraCells.AddRange(FlorBodyCells().Where(c => c != flor.CellId));
            Program.LogDebug($"[Raids] Fight #{fight.FightId}: the Flor Maldita on {flor.CellId} and {string.Join(", ", flor.ExtraCells)}.");
        }

        /// <summary>
        /// Whether a fighter changes sides for real (3107) in its fight: the Flor Maldita, whose
        /// camp is whose it heals and boosts each turn -- "Quand ce seuil est déclenché, elle se
        /// soigne entièrement et change d'équipe pour passer dans votre camp pour 2 tours".
        /// </summary>
        public static bool ChangesSideForReal(FightInstance fight, Fighter fighter)
            => fighter != null && fighter.IsMonster && fighter.MonsterId == Flor && IsHers(fight);

        /// <summary>A side-changer's own side: its summoner's, the Princesa's.</summary>
        public static int HomeSideOf(FightInstance fight, Fighter fighter)
            => fight?.Buscar(fighter.Invocador)?.TeamId ?? FightInstance.Rojos;

        // ─── Phase II ───────────────────────────────────────────────────────────────────────

        /// <summary>The Volonté de la Reine; the Flor's purple glyph's cells and its spell (32022 grade 2).</summary>
        public const int Volonte = 8280;
        public const int Glyph = 32022;
        public const int GlyphGrade = 2;

        /// <summary>Her phase II's marker, the Flor's on her side, and hers once vulnerable (on the Flor too).</summary>
        public const int PhaseTwo = 6663;
        public const int OnHerSide = 6665;
        public const int Vulnerable = 6666;

        /// <summary>The ring round the board's middle where the purple glyph goes: its spell's twenty fixed cells.</summary>
        public static IReadOnlyList<int> GlyphCells() => SpellEffects.De(Glyph, 1).SelectMany(e => e.CeldasFijas).Distinct().ToList();

        private static readonly ConcurrentDictionary<long, byte> _secondPhase = new();

        public static bool InSecondPhase(FightInstance fight) => fight != null && _secondPhase.ContainsKey(fight.FightId);

        /// <summary>
        /// Her scene's next phase: the Volonté de la Reine beside her, one of her side -- as the
        /// Reina's Volonté de la Princesse is in hers (inferred: the guide says so of the Reina's).
        /// </summary>
        public static async Task SecondPhaseAsync(NetworkStream stream, FightInstance fight, Fighter caster)
        {
            var princesa = PrincesaOf(fight);
            if (princesa == null || caster != princesa || !_secondPhase.TryAdd(fight.FightId, 0)) return;
            var volonte = await Handlers.FightHandler.SummonAsync(stream, fight, princesa, Volonte, 1, princesa.CellId);
            await Handlers.FightHandler.PromoteSummonAsync(stream, fight, volonte);
            Program.LogDebug($"[Raids] Fight #{fight.FightId}: the Princesa's phase II, the Volonté de la Reine in.");
        }

        /// <summary>
        /// A turn starts: the Flor's, in her phase II, on her side, and she still invulnerable, lays its
        /// purple glyph for a turn on a free cell of the ring -- what its turn-start script (32004
        /// grade 2) draws one of twenty cells for, through a cast on a fixed cell this engine aims
        /// elsewhere. "Ce glyphe ne peut être posé que sur les cases autour de la bordure du milieu".
        /// </summary>
        public static async Task OnTurnStartAsync(NetworkStream stream, FightInstance fight, Fighter fighter)
        {
            if (fighter == null || !fighter.IsMonster || fighter.MonsterId != Flor || !IsHers(fight)) return;
            if (!fighter.Buffs.TieneEstado(PhaseTwo) || !fighter.Buffs.TieneEstado(OnHerSide) || fighter.Buffs.TieneEstado(Vulnerable)) return;
            var walkable = MapManager.GetFightWalkable(fight.MapId);
            var free = GlyphCells().Where(c => !fight.Todos.Any(f => f.IsAlive && f.Occupies(c))
                                               && (walkable == null || walkable.Count == 0 || walkable.Contains(c))).ToList();
            if (free.Count == 0) return;
            int cell = free[Random.Shared.Next(free.Count)];
            await Handlers.FightHandler.CastAtAsync(stream, fight, fighter, Glyph, GlyphGrade, cell);
            Program.LogDebug($"[Raids] The Flor Maldita lays its purple glyph on {cell}.");
        }

        /// <summary>A fight over: what it held goes.</summary>
        public static void Forget(long fightId)
        {
            _started.TryRemove(fightId, out _);
            _secondPhase.TryRemove(fightId, out _);
        }
    }
}
