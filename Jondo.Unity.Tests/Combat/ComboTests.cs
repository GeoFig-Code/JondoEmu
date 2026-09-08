using System.Linq;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.World.Fights;
using Xunit;

namespace Jondo.Unity.Tests.Combat
{
    /// <summary>
    /// The Rogue's combo, read out of spell 20497 instead of written down.
    /// </summary>
    public class ComboTests
    {
        private static Fighter Bomba(int template = 3112) => new()
        {
            Id = -2, TeamId = 0, CellId = 270, MaxHP = 90, CurrentHP = 90,
            IsMonster = true, MonsterId = template, GradeIndex = 3, Invocador = 10,
            SummonCost = 0, JuegaTurno = false,
        };

        [Fact]
        public void The_ladder_runs_past_the_fifteen_the_sheet_names_and_flattens_at_the_top()
        {
            var escalera = Combo.Ladder();

            // La ficha dice "de 1 a 15", y son los quince que se ven. Pero la escalera del
            // hechizo tiene DIECIOCHO peldaños: los tres de arriba (2751, 2752, 2753) existen y
            // se pueden alcanzar, y los tres pagan lo mismo que el quince porque el hechizo 20500
            // se queda sin grados. O sea que el tope de la ficha es real, pero está en el
            // porcentaje y no en el número de peldaños.
            Assert.Equal(18, escalera.Count);
            Assert.Equal(2484, escalera[0]);
            Assert.Equal(new[] { 2751, 2752, 2753 }, escalera.Skip(15).ToArray());

            foreach (int estado in escalera.Skip(14))
            {
                var bomba = Bomba();
                bomba.Buffs.PonerEstado(estado);
                Assert.Equal(360, Combo.PercentOf(bomba));
            }
        }

        [Fact]
        public void The_percentages_are_the_ones_the_class_sheet_prints()
        {
            // I: 0%, II: 20%, III: 40%, IV: 60%, V: 80%, VI: 100%, VII: 120%, VIII: 140%,
            // IX: 160%, X: 190%, XI: 220%, XII: 250%, XIII: 280%, XIV: 320%, XV: 360%.
            int[] ficha = { 0, 20, 40, 60, 80, 100, 120, 140, 160, 190, 220, 250, 280, 320, 360 };
            var escalera = Combo.Ladder();

            for (int nivel = 1; nivel <= ficha.Length; nivel++)
            {
                var bomba = Bomba();
                bomba.Buffs.PonerEstado(escalera[nivel - 1]);
                Assert.Equal(nivel, Combo.LevelOf(bomba));
                Assert.Equal(ficha[nivel - 1], Combo.PercentOf(bomba));
            }
        }

        [Fact]
        public void A_bomb_with_no_state_carries_no_combo()
        {
            var bomba = Bomba();
            Assert.Equal(0, Combo.LevelOf(bomba));
            Assert.Equal(0, Combo.PercentOf(bomba));
            Assert.False(Combo.Carries(bomba));
        }

        [Fact]
        public void Casting_the_ladder_walks_one_rung_at_a_time()
        {
            var fight = new FightInstance(1, 1);
            var tymador = new Fighter { Id = 10, TeamId = 0, CellId = 300, MaxHP = 500, CurrentHP = 500 };
            var bomba = Bomba();
            fight.AddPlayer(tymador);
            fight.AddPlayer(bomba);

            for (int esperado = 1; esperado <= 5; esperado++)
            {
                EffectEngine.Resolver(fight, bomba, Combo.LadderSpell, 1, bomba,
                                      EffectEngine.AlLanzar, fight.RoundNumber,
                                      celdaApuntada: bomba.CellId);
                Assert.Equal(esperado, Combo.LevelOf(bomba));
            }

            // Y en el peldaño cinco pega un 80% más, que es lo que dice la ficha del Combo V.
            Assert.Equal(80, Combo.PercentOf(bomba));
        }

        [Fact]
        public void The_ladder_never_leaves_two_states_on_the_same_bomb()
        {
            var fight = new FightInstance(1, 1);
            var tymador = new Fighter { Id = 10, TeamId = 0, CellId = 300, MaxHP = 500, CurrentHP = 500 };
            var bomba = Bomba();
            fight.AddPlayer(tymador);
            fight.AddPlayer(bomba);

            var escalera = Combo.Ladder().ToHashSet();
            for (int vez = 0; vez < 6; vez++)
            {
                EffectEngine.Resolver(fight, bomba, Combo.LadderSpell, 1, bomba,
                                      EffectEngine.AlLanzar, fight.RoundNumber,
                                      celdaApuntada: bomba.CellId);
                Assert.Single(bomba.Buffs.Estados.Where(escalera.Contains));
            }
        }

        [Fact]
        public void Two_combos_a_turn_is_what_the_class_sheet_says()
        {
            Assert.Equal(2, FightHandler.CombosPorTurno);
        }
    }
}
