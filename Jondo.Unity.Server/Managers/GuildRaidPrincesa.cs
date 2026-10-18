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

        /// <summary>A fight over: what it held goes.</summary>
        public static void Forget(long fightId) => _started.TryRemove(fightId, out _);
    }
}
