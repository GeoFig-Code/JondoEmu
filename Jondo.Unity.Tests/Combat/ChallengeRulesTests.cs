using System.IO;
using System.Threading.Tasks;
using Jondo.Unity.Launcher;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.Tests.Economy;
using Jondo.Unity.World.Fights;
using Jondo.Unity.World.Maps;
using Xunit;

namespace Jondo.Unity.Tests.Combat
{
    /// <summary>
    /// Four of the generic challenges, by their descriptions: «Manos limpias» (no direct damage),
    /// «Cada uno con su monstruo» (nobody else's enemy), «Espejo» (an ally behind the one who falls)
    /// and how «El dos por uno» and the others are offered at all.
    /// </summary>
    public class ChallengeRulesTests
    {
        private static bool Available => File.Exists(Paths.Resolve("retos_3.6.10.10.json"));

        private static readonly object _once = new object();
        private static bool _loaded;

        private static void Load()
        {
            lock (_once)
            {
                if (_loaded) return;
                Challenges.Initialize();
                Challenges.OnlyOffer(ChallengeWatcher.Watched);
                _loaded = true;
            }
        }

        private static (FightInstance Fight, Fighter A, Fighter B, Fighter Enemy) Fight(int challenge, long id)
        {
            var fight = new FightInstance(id, 191105026);
            var a = new Fighter { Id = id + 1, TeamId = 0, CellId = 200, MaxHP = 100, CurrentHP = 100, Name = "Uno" };
            var b = new Fighter { Id = id + 2, TeamId = 0, CellId = 300, MaxHP = 100, CurrentHP = 100, Name = "Dos" };
            var enemy = new Fighter { Id = -1, TeamId = 1, CellId = 228, MaxHP = 100, CurrentHP = 100, IsMonster = true, Name = "Bicho" };
            fight.AddPlayer(a);
            fight.AddPlayer(b);
            fight.AddMonster(enemy);
            fight.AddMonster(new Fighter { Id = -2, TeamId = 1, CellId = 400, MaxHP = 100, CurrentHP = 100, IsMonster = true, Name = "Otro" });
            fight.ChallengesFixed.Add((challenge, 50));
            return (fight, a, b, enemy);
        }

        [Fact]
        public void The_four_are_offered()
        {
            if (!Available) return;
            Load();
            foreach (int id in new[] { ChallengeWatcher.ManosLimpias, ChallengeWatcher.DosPorUno,
                                       ChallengeWatcher.CadaUnoConSuMonstruo, ChallengeWatcher.Espejo })
                Assert.Equal(id, Challenges.KindOf(id));
        }

        [Fact]
        public async Task Clean_hands_breaks_on_a_direct_hit_only()
        {
            if (!Available) return;
            Load();
            await using var pipe = await ClientPipe.OpenAsync(8_960_100_001, 8_960_100_001, 191105026, "Uno");
            var (fight, a, _, enemy) = Fight(ChallengeWatcher.ManosLimpias, 8_960_100_000);

            await ChallengeWatcher.DamagedAsync(pipe.ToClient, fight, enemy, 10, a, 1, direct: false);   // a trap, a poison
            Assert.DoesNotContain(ChallengeWatcher.ManosLimpias, fight.ChallengesBroken);
            await ChallengeWatcher.DamagedAsync(pipe.ToClient, fight, enemy, 10, a, 1, direct: true);
            Assert.Contains(ChallengeWatcher.ManosLimpias, fight.ChallengesBroken);
        }

        [Fact]
        public async Task Each_his_own_monster_breaks_when_another_ally_hits_it()
        {
            if (!Available) return;
            Load();
            await using var pipe = await ClientPipe.OpenAsync(8_960_100_011, 8_960_100_011, 191105026, "Uno");
            var (fight, a, b, enemy) = Fight(ChallengeWatcher.CadaUnoConSuMonstruo, 8_960_100_010);

            await ChallengeWatcher.DamagedAsync(pipe.ToClient, fight, enemy, 10, a, 1);
            await ChallengeWatcher.DamagedAsync(pipe.ToClient, fight, enemy, 10, a, 1);
            Assert.DoesNotContain(ChallengeWatcher.CadaUnoConSuMonstruo, fight.ChallengesBroken);
            await ChallengeWatcher.DamagedAsync(pipe.ToClient, fight, enemy, 10, b, 1);
            Assert.Contains(ChallengeWatcher.CadaUnoConSuMonstruo, fight.ChallengesBroken);
        }

        [Fact]
        public async Task Mirror_wants_an_ally_behind_the_one_who_falls()
        {
            if (!Available) return;
            Load();
            await using var pipe = await ClientPipe.OpenAsync(8_960_100_021, 8_960_100_021, 191105026, "Uno");

            var (fight, a, b, enemy) = Fight(ChallengeWatcher.Espejo, 8_960_100_020);
            b.CellId = MapGeometry.Reflejar(a.CellId, enemy.CellId);
            Assert.True(b.CellId >= 0);
            enemy.CurrentHP = 0;
            await ChallengeWatcher.DiedAsync(pipe.ToClient, fight, enemy, false, a);
            Assert.DoesNotContain(ChallengeWatcher.Espejo, fight.ChallengesBroken);

            var (lonely, a2, _, enemy2) = Fight(ChallengeWatcher.Espejo, 8_960_100_030);
            enemy2.CurrentHP = 0;
            await ChallengeWatcher.DiedAsync(pipe.ToClient, lonely, enemy2, false, a2);
            Assert.Contains(ChallengeWatcher.Espejo, lonely.ChallengesBroken);
        }
    }
}
