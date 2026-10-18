using System.Linq;
using System.Threading.Tasks;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.World.Fights;
using Jondo.Unity.World.Maps;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The Reina Escarlata's jails as the client's data lays them out, and the fight's start: the
    /// players in the jails, two monsters in each one with somebody, a Floráculo before each, and
    /// everyone out and healed once the last of those monsters falls. In the MapManager collection,
    /// as every test that loads the maps.
    /// </summary>
    [Collection("MapManager")]
    public class GuildRaidReinaTests
    {
        private const long Arena = 238295313;

        private static bool HasData => GuildRaidReina.Jails().Count == 4;

        /// <summary>I top left to IV bottom right: their glyphs, exits, markers, Floráculos and cages.</summary>
        [Fact]
        public void The_jails_are_read_off_the_client_s_spells()
        {
            if (!HasData) return;
            var jails = GuildRaidReina.Jails();
            Assert.Equal(new[] { 31992, 32747, 32748, 32749 }, jails.Select(j => j.Glyph));
            Assert.Equal(new[] { 201, 204, 369, 372 }, jails.Select(j => j.Exit));
            Assert.Equal(new[] { 7110, 7111, 7112, 7113 }, jails.Select(j => j.Marker));
            Assert.Equal(new[] { (8289, 173), (8290, 190), (8292, 397), (8291, 386) }, jails.Select(j => (j.Floracle, j.FloracleCell)));
            Assert.Equal(new[] { (11, 12), (21, 22), (41, 42), (31, 32) }, jails.Select(j => (j.CageUp, j.CageDown)));
            Assert.Equal(new[] { 8285, 8286, 8287, 8288 }, jails.Select(j => j.Kind));
            Assert.All(jails, j => Assert.Equal(16, j.Cells.Count));
            Assert.Equal(1, GuildRaidReina.JailAt(128).Number);
        }

        /// <summary>The arena's own placement: two red cells in each jail, the nine blue in the middle.</summary>
        [Fact]
        public void The_players_start_in_the_jails()
        {
            if (!HasData) return;
            var (red, blue) = GuildRaidStagings.PlacementOf(Arena).Value;
            Assert.Equal(8, red.Count);
            Assert.All(GuildRaidReina.Jails(), j => Assert.Equal(2, red.Count(j.Cells.Contains)));
            Assert.Equal(9, blue.Count);
            Assert.All(blue, c => Assert.Null(GuildRaidReina.JailAt(c)));
        }

        private static (FightInstance Fight, Fighter Reina, Fighter[] People) Fight()
        {
            if (!MapManager.WalkableCells.ContainsKey(Arena)) MapManager.Initialize();
            MobSpawnManager.EnsureMonsterData();
            var fight = new FightInstance(9701, 238295316, Arena);
            // Four players, two in jail I and two in jail II.
            var people = new[] { 128, 129, 122, 150 }.Select((c, i) => new Fighter
            {
                Id = 7001 + i, TeamId = 0, CellId = c, MaxHP = 5000, CurrentHP = 1000 + i, Level = 200,
            }).ToArray();
            foreach (var person in people) fight.AddPlayer(person);
            var reina = new Fighter { Id = -1, TeamId = 1, CellId = 272, MaxHP = 50000, CurrentHP = 50000, IsMonster = true,
                                      MonsterId = GuildRaidReina.Reina, Level = 200 };
            fight.AddMonster(reina);
            fight.StartFight();
            return (fight, reina, people);
        }

        /// <summary>
        /// Four players in two jails: the four Floráculos at their doors, two monsters of each jail's
        /// kind in the two jails with somebody, sharing damage, none in the other two; the last of
        /// them fallen, everyone out at his jail's exit, healed in full, and the Reina's pass gone.
        /// </summary>
        [Fact]
        public async Task The_jails_open_once_their_monsters_fall()
        {
            if (!HasData) return;
            var (fight, reina, people) = Fight();
            await FightHandler.AplicarEfectosAsync(null, fight, reina, 31924, 1, reina, EffectEngine.AlLanzar, reina.CellId);
            bool passes = reina.Buffs.Puestos.Any(b => b.HechizoOrigen == GuildRaidReina.PassTurns);
            try
            {
                await GuildRaidReina.OnFightStartedAsync(null, fight);
                Assert.True(GuildRaidReina.InJailPhase(fight));

                var jails = GuildRaidReina.Jails();
                foreach (var jail in jails)
                {
                    var floracle = fight.Rojo.Single(m => m.MonsterId == jail.Floracle);
                    Assert.Equal(jail.FloracleCell, floracle.CellId);
                    Assert.True(floracle.EsInvocado);
                    Assert.False(floracle.JuegaTurno);       // "une entité statique qui n'a pas de tour de jeu"
                }
                var inJails = fight.Rojo.Where(m => GuildRaidReina.JailKinds.Contains(m.MonsterId)).ToList();
                Assert.Equal(4, inJails.Count);
                Assert.All(inJails, m => Assert.True(m.JuegaTurno));
                Assert.Equal(2, inJails.Count(m => m.MonsterId == jails[0].Kind && jails[0].Cells.Contains(m.CellId)));
                Assert.Equal(2, inJails.Count(m => m.MonsterId == jails[1].Kind && jails[1].Cells.Contains(m.CellId)));
                var first = inJails.First(m => m.MonsterId == jails[0].Kind);
                Assert.Contains(inJails.Single(m => m != first && m.MonsterId == first.MonsterId),
                                FightHandler.Enlazados(fight, first, new[] { "D" }));

                foreach (var monster in inJails.Take(3))
                {
                    monster.CurrentHP = 0;
                    await FightHandler.CaenSusInvocadosAsync(null, fight, monster);
                }
                Assert.True(GuildRaidReina.InJailPhase(fight));
                inJails[3].CurrentHP = 0;
                await FightHandler.CaenSusInvocadosAsync(null, fight, inJails[3]);

                Assert.False(GuildRaidReina.InJailPhase(fight));
                foreach (var person in people)
                {
                    Assert.Null(GuildRaidReina.JailAt(person.CellId));
                    Assert.Equal(person.MaxHP, person.CurrentHP);
                }
                Assert.True(MapGeometry.Distance(people[0].CellId, jails[0].Exit) <= 2);
                Assert.True(MapGeometry.Distance(people[2].CellId, jails[1].Exit) <= 2);
                if (passes) Assert.DoesNotContain(reina.Buffs.Puestos, b => b.HechizoOrigen == GuildRaidReina.PassTurns);
            }
            finally
            {
                GuildRaidReina.Forget(fight.FightId);
            }
        }

        /// <summary>The Reina out of her jail phase, with her script on and the Floráculos at their doors; players in the middle.</summary>
        private static async Task<(FightInstance Fight, Fighter Reina, Fighter[] People)> Unjailed(int people)
        {
            if (!MapManager.WalkableCells.ContainsKey(Arena)) MapManager.Initialize();
            MobSpawnManager.EnsureMonsterData();
            var fight = new FightInstance(9702 + people, 238295316, Arena);
            var cells = new[] { 243, 244, 245, 271 };
            var players = Enumerable.Range(0, people).Select(i => new Fighter
            {
                Id = 7101 + i, TeamId = 0, CellId = cells[i], MaxHP = 5000, CurrentHP = 5000, Level = 200,
            }).ToArray();
            foreach (var person in players) fight.AddPlayer(person);
            var reina = new Fighter { Id = -1, TeamId = 1, CellId = 300, MaxHP = 50000, CurrentHP = 50000, IsMonster = true,
                                      MonsterId = GuildRaidReina.Reina, Level = 200 };
            fight.AddMonster(reina);
            GuildRaidReina.OnFightCreated(fight);
            fight.StartFight();
            reina.Conducta = (31924, 1);                               // her "Aura Escarlata", as a fight casts it
            await FightHandler.LanzarLaConductaAsync(null, fight, reina);
            await GuildRaidReina.OnFightStartedAsync(null, fight);     // nobody in a jail: no monsters, open at once
            return (fight, reina, players);
        }

        /// <summary>
        /// The Exilio for jail I sends the one hit into it, with its marker and his pass; its Floráculo
        /// at its threshold lets him out at the jail's exit, his marker and pass gone, Évadé and
        /// immune for a while, and jail I free again for the Reina.
        /// </summary>
        [Fact]
        public async Task The_exilio_jails_and_the_floraculo_frees()
        {
            if (!HasData) return;
            var (fight, reina, people) = await Unjailed(2);
            try
            {
                Assert.False(GuildRaidReina.InJailPhase(fight));
                Assert.Same(reina, fight.SceneStandIn);
                var jail = GuildRaidReina.Jails()[0];
                var jailed = people[0];

                await FightHandler.CastAtAsync(null, fight, reina, 31937, 1, jailed.CellId);
                Assert.True(GuildRaidReina.Jailed(jailed));
                Assert.Equal(jail.Cell, jailed.CellId);
                Assert.True(reina.Buffs.TieneEstado(6626));            // jail I taken
                var floracle = fight.Rojo.Single(m => m.MonsterId == jail.Floracle);
                Assert.True(floracle.Buffs.TieneEstado(6635));          // open to blows
                Assert.True(jailed.Buffs.Puestos.Any(b => b.HechizoOrigen == 31942));
                Assert.Equal(-1, fight.WonByEffect);                    // the other is still out

                await FightHandler.CastAtAsync(null, fight, floracle, GuildRaidReina.FloracleThreshold, 1, floracle.CellId);
                Assert.False(GuildRaidReina.Jailed(jailed));
                Assert.Null(GuildRaidReina.JailAt(jailed.CellId));
                Assert.True(MapGeometry.Distance(jailed.CellId, jail.Exit) <= 1);
                Assert.True(jailed.Buffs.TieneEstado(6631));            // immune to the Exilio
                Assert.True(jailed.Buffs.TieneEstado(7157));            // Évadé
                Assert.DoesNotContain(jailed.Buffs.Puestos, b => b.HechizoOrigen == 31942);
                Assert.False(reina.Buffs.TieneEstado(6626));
            }
            finally
            {
                GuildRaidReina.Forget(fight.FightId);
            }
        }

        /// <summary>Everybody jailed loses the fight; nobody jailed, the Reina's end-of-fight check kills nobody.</summary>
        [Fact]
        public async Task Everybody_jailed_loses()
        {
            if (!HasData) return;
            var (fight, reina, people) = await Unjailed(2);
            try
            {
                for (int i = 0; i < 3; i++) await FightHandler.DispararAsync(null, fight, reina, EffectEngine.AlEmpezarElTurno);
                Assert.All(people, p => Assert.True(p.IsAlive));

                await FightHandler.CastAtAsync(null, fight, reina, 31937, 1, people[0].CellId);
                Assert.Equal(-1, fight.WonByEffect);
                await FightHandler.CastAtAsync(null, fight, reina, 31973, 1, people[1].CellId);
                Assert.True(GuildRaidReina.Jailed(people[1]));
                Assert.Equal(GuildRaidReina.Jails()[1].Cell, people[1].CellId);
                Assert.Equal(FightInstance.Rojos, fight.WonByEffect);
            }
            finally
            {
                GuildRaidReina.Forget(fight.FightId);
            }
        }

        /// <summary>
        /// Her phase II: whoever stands where the Pommeau appears dies; the Volonté de la Princesse
        /// comes in beside her, one of her side to beat, the Pommeau and the Lame on their cells with no
        /// turn of their own; she is healed in full, invulnerable, and plays twice a round. The Pommeau
        /// and the Lame beaten, she is open to blows with their bonuses.
        /// </summary>
        [Fact]
        public async Task Her_second_phase_brings_the_volonte_and_the_swords()
        {
            if (!HasData) return;
            var (fight, reina, people) = await Unjailed(2);
            try
            {
                var cells = GuildRaidReina.SwordCells();
                Assert.Equal(2, cells.Count);
                people[1].MoverA(cells[0]);
                Assert.Contains(reina.Buffs.Puestos, r => r.EffectId == EffectEngine.Umbral);   // her 1-life threshold, on from the start
                reina.CurrentHP = 1;

                await FightHandler.DispararAsync(null, fight, reina, "TR31977");   // her 1-life threshold reached
                Assert.True(GuildRaidReina.InSecondPhase(fight));
                Assert.False(people[1].IsAlive);                       // "il est OS"
                Assert.Equal(reina.MaxHP, reina.CurrentHP);
                Assert.True(reina.Buffs.TieneEstado(56));
                Assert.Equal(50, fight.TwiceARound[reina.Id]);

                var volonte = fight.Rojo.Single(m => m.MonsterId == GuildRaidReina.Volonte);
                Assert.False(volonte.EsInvocado);
                Assert.True(volonte.JuegaTurno);
                Assert.True(MapGeometry.Distance(volonte.CellId, reina.CellId) <= 2);
                var pommeau = fight.Rojo.Single(m => m.MonsterId == GuildRaidReina.Pommeau);
                var lame = fight.Rojo.Single(m => m.MonsterId == GuildRaidReina.Lame);
                Assert.Equal((cells[0], cells[1]), (pommeau.CellId, lame.CellId));
                Assert.False(pommeau.JuegaTurno);
                Assert.True(pommeau.Buffs.TieneEstado(56) && lame.Buffs.TieneEstado(56));

                pommeau.CurrentHP = 0;
                await FightHandler.CaenSusInvocadosAsync(null, fight, pommeau);
                Assert.True(reina.Buffs.TieneEstado(56));
                lame.CurrentHP = 0;
                await FightHandler.CaenSusInvocadosAsync(null, fight, lame);
                Assert.False(reina.Buffs.TieneEstado(56));
                Assert.True(reina.Buffs.TieneEstado(6643) && reina.Buffs.TieneEstado(6644));
            }
            finally
            {
                GuildRaidReina.Forget(fight.FightId);
            }
        }
    }
}
