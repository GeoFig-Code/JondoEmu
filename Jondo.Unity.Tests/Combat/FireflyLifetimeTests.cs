using System.Linq;
using System.Threading.Tasks;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.World.Fights;
using Xunit;

namespace Jondo.Unity.Tests.Combat
{
    /// <summary>
    /// The Aniripsa's fireflies "mueren al mínimo daño recibido, después de haber utilizado un
    /// hechizo o al final de su segundo turno de juego" (the class sheet). Their own spell --
    /// Fantasía for the fairy, Locura for the fire -- kills them with a 141 under TE and a delay
    /// of one: held back at the end of their first turn, at once at the end of their second.
    /// </summary>
    public class FireflyLifetimeTests
    {
        private const int Fantasia = 25769;
        private const int Locura = 25812;

        private static Fighter Firefly(FightInstance fight, long id, int spell, int givenInRound)
        {
            var firefly = new Fighter
            {
                Id = id, TeamId = 0, CellId = 300 + (int)-id, MaxHP = 315, CurrentHP = 315, Level = 200,
                MaxAP = 6, CurrentAP = 6, MaxMP = 3, CurrentMP = 3, Invocador = 7001,
            };
            fight.AddPlayer(firefly);
            firefly.Buffs.PonerActitud(spell, givenInRound: givenInRound);
            return firefly;
        }

        [Theory]
        [InlineData(Fantasia)]
        [InlineData(Locura)]
        public async Task A_firefly_outlives_its_first_turn_and_dies_at_the_end_of_its_second(int spell)
        {
            if (SpellEffects.De(spell, 1).Count == 0) return;
            var fight = new FightInstance(1, 1);
            var aniripsa = new Fighter { Id = 7001, TeamId = 0, CellId = 200, MaxHP = 2000, CurrentHP = 2000, Level = 200 };
            fight.AddPlayer(aniripsa);
            var bornNow = Firefly(fight, -1, spell, fight.RoundNumber);
            var bornLastRound = Firefly(fight, -2, spell, fight.RoundNumber - 1);

            await FightHandler.DispararAsync(null, fight, bornNow, EffectEngine.AlAcabarElTurno);
            await FightHandler.DispararAsync(null, fight, bornLastRound, EffectEngine.AlAcabarElTurno);

            Assert.True(bornNow.IsAlive);
            Assert.DoesNotContain(bornNow.Buffs.Puestos, b => b.Pendiente && b.EffectId == 141);
            Assert.False(bornLastRound.IsAlive);
        }
    }
}
