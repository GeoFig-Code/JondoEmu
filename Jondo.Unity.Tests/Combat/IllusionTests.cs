using System;
using System.Linq;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.World.Combat;
using Jondo.Unity.World.Fights;
using Xunit;

namespace Jondo.Unity.Tests.Combat
{
    /// <summary>
    /// Tymadura's copies (effect 1097) on the geometry of its capture: the Tymador on 230 aims
    /// at 257, lands there, and copies appear on 259, 201 and 203.
    /// </summary>
    public class IllusionTests
    {
        private const int Tymadura = 13431;
        private const long Rogue = 53721497699;

        private static readonly byte[] CapturedLook = Convert.FromHexString(
            "0a18e8b6960ff2ecd110b986ed1980b8be27aca4b12ba9f3fa35100318012a013a3204fd0aa011");

        private static (FightInstance Fight, Fighter Me) Board()
        {
            var fight = new FightInstance(1, 1);
            var me = new Fighter { Id = Rogue, TeamId = 0, CellId = 230, MaxHP = 2000, CurrentHP = 2000, Level = 200 };
            fight.AddPlayer(me);
            var enemy = new Fighter { Id = 20, TeamId = 1, CellId = 400, MaxHP = 500, CurrentHP = 500, Level = 50 };
            fight.AddOpponent(enemy);
            return (fight, me);
        }

        [Fact]
        public void The_caster_jumps_and_three_copies_appear_two_steps_down_each_free_axis()
        {
            var (fight, me) = Board();

            var outcomes = EffectEngine.Resolver(fight, me, Tymadura, 3, me, EffectEngine.AlLanzar,
                                                 fight.RoundNumber, celdaApuntada: 257);

            Assert.Equal(257, me.CellId);
            var made = Assert.Single(outcomes, o => o.Ilusiones != null);
            Assert.Equal(230, made.CasillaDesde);
            Assert.Equal(257, made.CasillaHasta);
            Assert.Equal(new[] { 259, 201, 203 }, made.Ilusiones.Select(i => i.CellId));
            Assert.Equal(3, me.Ilusiones.Count);
            Assert.All(made.Ilusiones, i =>
            {
                Assert.True(i.EsIlusion);
                Assert.False(i.JuegaTurno);
                Assert.Equal(Rogue, i.Invocador);
                Assert.Equal(1050, i.MaxHP);
                Assert.DoesNotContain(i, fight.TurnOrder);
            });
            Assert.Contains(outcomes, o => o.AcabaElTurno);
        }

        /// <summary>The first copy of the capture, byte for byte: -8 on 259 facing 1, the original on 230 facing 3.</summary>
        [Fact]
        public void An_illusion_block_is_the_captures()
        {
            byte[] frame = FightProtocol.BuildIllusion(Rogue, -8, 259, 1, 230, 3,
                                                       FightProtocol.IllusionSheet(200), CapturedLook);

            Assert.Equal(
                "0adb0312d80312d5030a070883021001200012be0312920312f90218022a060801120210052a060817120210042a04082512002a04082112002a04082312002a04082412002a04082212002a04083a12002a04083612002a04083812002a04083912002a04083712002a04085512002a04085712002a04086512002a06081b1202100a2a06081c1202100a2a06085d120210032a06084f1202100a2a06084e1202100a2a051203109a082a06080a120210642a06080b120210642a06080d120210642a06080e120210642a06080f120210642a04081012002a04081212002a060813120210012a04081912002a06081a120210012a04083212002a06084b1202100a2a04085812002a04085912002a04085a12002a04085b12002a04085c12002a04085f12002a04086012002a04086112002a04086612002a06086b120210642a07089601120210642a060878120210642a060879120210642a06087a120210642a06087b120210642a06087c120210642a06087d120210642a07088d01120210642a07088e01120210642a07088f01120210643a14180122100a0708e6011003200018e380b490c8011a270a18e8b6960ff2ecd110b986ed1980b8be27aca4b12ba9f3fa35100318012a013a3204fd0aa01118f8ffffffffffffffff0118e380b490c80170c908",
                Hex(frame));
        }

        [Fact]
        public void The_other_frames_of_the_capture()
        {
            Assert.Equal("18e380b490c801709601920209080120e380b490c801",
                         Hex(FightProtocol.BuildVisibility(Rogue, Rogue, FightProtocol.Hidden)));
            Assert.Equal("18e380b490c801709601920209080220e380b490c801",
                         Hex(FightProtocol.BuildVisibility(Rogue, Rogue, FightProtocol.Visible)));
            Assert.Equal("18e380b490c80170049a020a08810210e380b490c801",
                         Hex(FightProtocol.BuildTeleport(Rogue, Rogue, 257)));
            Assert.Equal("18e380b490c801520b08faffffffffffffffff01708508",
                         Hex(FightProtocol.BuildIllusionGone(Rogue, -6)));
            Assert.Equal("121108840210f5ffffffffffffffff0118d50218e380b490c8017008",
                         Hex(FightProtocol.BuildSwap(Rogue, 260, -11, 341)));
        }

        /// <summary>A copy holds its cell: the next cast finds it there and the caster cannot land on it.</summary>
        [Fact]
        public void A_copy_holds_its_cell()
        {
            var (fight, me) = Board();
            EffectEngine.Resolver(fight, me, Tymadura, 3, me, EffectEngine.AlLanzar, fight.RoundNumber, celdaApuntada: 257);

            var again = EffectEngine.Resolver(fight, me, Tymadura, 3, me, EffectEngine.AlLanzar, fight.RoundNumber, celdaApuntada: 259);

            Assert.DoesNotContain(again, o => o.Ilusiones != null);
            Assert.Equal(257, me.CellId);
        }

        private static string Hex(byte[] bytes) => string.Concat(bytes.Select(b => b.ToString("x2")));
    }
}
