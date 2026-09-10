using System.Collections.Generic;
using System.Linq;
using Jondo.Unity.Server.Network;
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
        public void Only_a_bomb_can_carry_a_combo()
        {
            // Polvora y Mosquete encadenan el hechizo del combo con mascaras que el motor no sabe
            // estrechar, y sin esto caia en el lanzador: el tymador salia con Combo IV en su panel
            // y la bomba se quedaba igual.
            var fight = new FightInstance(1, 1);
            var tymador = new Fighter { Id = 10, TeamId = 0, CellId = 300, MaxHP = 500, CurrentHP = 500 };
            fight.AddPlayer(tymador);

            EffectEngine.Resolver(fight, tymador, Combo.LadderSpell, 1, tymador,
                                  EffectEngine.AlLanzar, fight.RoundNumber,
                                  celdaApuntada: tymador.CellId);

            Assert.Equal(0, Combo.LevelOf(tymador));
            Assert.False(Combo.Carries(tymador));
        }

        [Fact]
        public void A_bomb_never_climbs_past_the_fifteenth_rung()
        {
            var fight = new FightInstance(1, 1);
            var tymador = new Fighter { Id = 10, TeamId = 0, CellId = 300, MaxHP = 500, CurrentHP = 500 };
            var bomba = Bomba();
            fight.AddPlayer(tymador);
            fight.AddPlayer(bomba);

            for (int vez = 0; vez < 25; vez++)
            {
                EffectEngine.Resolver(fight, bomba, Combo.LadderSpell, 1, bomba,
                                      EffectEngine.AlLanzar, fight.RoundNumber,
                                      celdaApuntada: bomba.CellId);
            }

            Assert.Equal(Combo.Tope, Combo.LevelOf(bomba));
            Assert.Equal(360, Combo.PercentOf(bomba));
        }

        [Fact]
        public void A_bomb_holds_exactly_one_rung_at_a_time()
        {
            var fight = new FightInstance(1, 1);
            var tymador = new Fighter { Id = 10, TeamId = 0, CellId = 300, MaxHP = 500, CurrentHP = 500 };
            var bomba = Bomba();
            fight.AddPlayer(tymador);
            fight.AddPlayer(bomba);

            var escalera = Combo.Ladder().ToHashSet();
            for (int vez = 1; vez <= 6; vez++)
            {
                EffectEngine.Resolver(fight, bomba, Combo.LadderSpell, 1, bomba,
                                      EffectEngine.AlLanzar, fight.RoundNumber,
                                      celdaApuntada: bomba.CellId);
                Assert.Single(bomba.Buffs.Estados.Where(escalera.Contains));
                Assert.Equal(vez, Combo.LevelOf(bomba));
            }
        }

        /// <summary>
        /// The bomb announcing its own combo cast, byte for byte against frame 262 of
        /// "tymador-explobomba resiliente.pcapng": the bomb -5, on cell 216, casting 20497 at
        /// level 54099, with itself and its Rogue in the affected list.
        /// </summary>
        /// <remarks>
        /// Two f4 and no f8 is what tells it apart from an ordinary cast, and why it does not go
        /// through CastAt: that one writes a single f4 and closes with f8 = 1.
        /// </remarks>
        [Fact]
        public void A_bomb_announces_the_combo_it_casts_on_itself()
        {
            byte[] paquete = FightProtocol.BuildComboCast(
                bomb: -5, owner: 53721497699, cell: 216, spell: 20497, levelId: 54099);

            Assert.Equal("18fbffffffffffffffff013a2e10fbffffffffffffffff01220b20fbffffffff" +
                         "ffffffff01220720e380b490c80130d8013a081091a00118d3a60370ac02",
                         Hex(paquete));
        }

        /// <summary>And the same with the grade of 20500 behind it: frame 264.</summary>
        [Fact]
        public void And_the_bonus_spell_right_behind_it()
        {
            byte[] paquete = FightProtocol.BuildComboCast(
                bomb: -5, owner: 53721497699, cell: 216, spell: 20500, levelId: 54103);

            Assert.Equal("18fbffffffffffffffff013a2e10fbffffffffffffffff01220b20fbffffffff" +
                         "ffffffff01220720e380b490c80130d8013a081094a00118d7a60370ac02",
                         Hex(paquete));
        }

        /// <summary>
        /// The look of a growing bomb, byte for byte against frame 259: bone 1562 at scale 105.
        /// </summary>
        [Fact]
        public void A_bomb_grows_by_the_scale_of_its_look()
        {
            byte[] aspecto = FightProtocol.WithScale(
                Pb.New().Var(2, 3).Var(3, 1562).Build(), 105);

            Assert.Equal("1003189a0c2a0169", Hex(aspecto));

            byte[] paquete = FightProtocol.BuildLookChanged(-5, aspecto);
            Assert.Equal("18fbffffffffffffffff01709501d2011508fbffffffffffffffff011a081003" +
                         "189a0c2a0169", Hex(paquete));
        }

        /// <summary>
        /// And the size is the sum of the 1060 buffs, which is why it jumps by fifteen and twenty
        /// and not by a fixed step.
        /// </summary>
        /// <remarks>
        /// Rung 4 in the capture is scale 125, and 125 is 100 + 10 + 15: the 1060 of grade 2 and
        /// the one of grade 3, both alive at once. Checked over every look change in the Rogue
        /// captures: 540 of them come out exactly this way.
        /// </remarks>
        [Fact]
        public void The_size_is_a_hundred_plus_the_live_1060_buffs()
        {
            var bomba = Bomba();
            Assert.Equal(100, Combo.SizeOf(bomba, 1));

            bomba.Buffs.Poner(new Buff { EffectId = 1060, Cuanto = 10, EffectUid = 1,
                                         CaducaEnRonda = -1 }, () => 1);
            bomba.Buffs.Poner(new Buff { EffectId = 1060, Cuanto = 15, EffectUid = 2,
                                         CaducaEnRonda = -1 }, () => 2);

            Assert.Equal(125, Combo.SizeOf(bomba, 1));

            // Y nunca mas de dos vivos: el tercero desaloja al primero.
            bomba.Buffs.Poner(new Buff { EffectId = 1060, Cuanto = 20, EffectUid = 3,
                                         CaducaEnRonda = -1 }, () => 3);
            Assert.Equal(135, Combo.SizeOf(bomba, 1));
        }

        /// <summary>The grade of 20500 that pays for each rung, read off the ladder.</summary>
        [Fact]
        public void Every_rung_names_the_grade_that_pays_for_it()
        {
            Assert.Equal(1, Combo.GradeOf(2));
            Assert.Equal(2, Combo.GradeOf(3));
            Assert.Equal(10, Combo.GradeOf(11));
        }

        private static string Hex(byte[] bytes)
            => string.Concat(bytes.Select(b => b.ToString("x2")));

        [Fact]
        public void Two_combos_a_turn_is_what_the_class_sheet_says()
        {
            Assert.Equal(2, FightHandler.CombosPorTurno);
        }

        /// <summary>
        /// Ningún número de embrujo se repite en una bomba, y los que se van se anuncian todos.
        /// </summary>
        /// <remarks>
        /// Es lo que dejaba la bomba en Combo I. Volver a poner el peldaño que ya llevaba caía en
        /// la rama de «éste ya estaba» y devolvía el MISMO número, así que al cliente le llegaba
        /// dos veces el embrujo 1 con el estado 2484 y una sola retirada; el estado sobrante se
        /// quedaba puesto y es el nombre del estado —«Combo I»— lo que el cliente pinta.
        ///
        /// El servidor real no repite un número ni una vez: en «tymador-explobomba resiliente» la
        /// misma bomba lleva el 2484 en el embrujo 19 y otra vez en el 23, y en los frames 260 y
        /// 261 los quita los dos.
        /// </remarks>
        [Fact]
        public void A_bomb_never_gets_the_same_buff_number_twice()
        {
            var fight = new FightInstance(1, 1);
            var tymador = new Fighter { Id = 10, TeamId = 0, CellId = 300, MaxHP = 500, CurrentHP = 500 };
            var bomba = Bomba();
            fight.AddPlayer(tymador);
            fight.AddPlayer(bomba);

            var puestos = new List<int>();
            var quitados = new List<int>();
            for (int vez = 0; vez < 8; vez++)
            {
                foreach (var c in EffectEngine.Resolver(fight, bomba, Combo.LadderSpell, 1, bomba,
                                                        EffectEngine.AlLanzar, fight.RoundNumber,
                                                        celdaApuntada: bomba.CellId))
                {
                    if (c.Buff != null && Combo.EsPeldano(c.Buff.Estado)) puestos.Add(c.Buff.Numero);
                    foreach (var ido in c.BuffsQuitados)
                    {
                        if (Combo.EsPeldano(ido.Estado)) quitados.Add(ido.Numero);
                    }
                }
            }

            Assert.Equal(puestos.Count, puestos.Distinct().Count());

            // Y de todos los que se pusieron sólo sigue vivo el último: los demás se anunciaron.
            Assert.Equal(puestos.Count - 1, quitados.Distinct().Count());
            Assert.DoesNotContain(puestos[^1], quitados);
        }
    }
}
