using System.Linq;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.World.Fights;
using Xunit;

namespace Jondo.Unity.Tests.Combat
{
    /// <summary>
    /// A fighter who plays twice a round (4059), the Defensor de la Reserva once its companions are
    /// gone: "une première fois au début du tour puis une deuxième fois au milieu du tour (après 2
    /// personnages). Si les personnages qui jouent entre ses 2 tours sont éliminés, il jouera 2 fois
    /// de suite" (DPLN). The timeline shows the second as an additional turn.
    /// </summary>
    public class TwiceARoundTests
    {
        private const int Defensor = 8319;

        /// <summary>The Defensor first by initiative, four characters behind it.</summary>
        private static (FightInstance Fight, Fighter Defensor, Fighter[] People) Fight()
        {
            var fight = new FightInstance(1, 1);
            var people = Enumerable.Range(1, 4)
                .Select(i => new Fighter { Id = 7000 + i, TeamId = 0, CellId = 200 + i, MaxHP = 5000, CurrentHP = 5000, Initiative = 100 - i })
                .ToArray();
            foreach (var person in people) fight.AddPlayer(person);
            var defensor = new Fighter { Id = -1, TeamId = 1, CellId = 300, MaxHP = 20000, CurrentHP = 20000, IsMonster = true,
                                         MonsterId = Defensor, Initiative = 1000 };
            fight.AddMonster(defensor);
            fight.StartFight();
            return (fight, defensor, people);
        }

        /// <summary>Plays the round's turns from the current one to the Defensor's next regular one.</summary>
        private static void ToItsNextTurn(FightInstance fight, Fighter defensor)
        {
            do fight.NextTurn(); while (fight.CurrentFighter != defensor);
        }

        [Fact]
        public void Its_second_turn_comes_after_two_of_four()
        {
            var (fight, defensor, people) = Fight();
            Assert.Same(defensor, fight.CurrentFighter);
            fight.PlayTwiceARound(defensor.Id, 50);

            ToItsNextTurn(fight, defensor);
            Assert.Equal(people[1].Id, fight.Additional.After);
            Assert.Null(fight.PlayingAdditional);

            Assert.Same(people[0], fight.NextTurn());
            Assert.Same(people[1], fight.NextTurn());
            Assert.Same(defensor, fight.NextTurn());
            Assert.NotNull(fight.PlayingAdditional);
            Assert.True(fight.TurnSlot < 0);
            Assert.Same(people[2], fight.NextTurn());
            Assert.Null(fight.PlayingAdditional);
            Assert.Same(people[3], fight.NextTurn());
            int round = fight.RoundNumber;
            Assert.Same(defensor, fight.NextTurn());
            Assert.Equal(round + 1, fight.RoundNumber);
            Assert.Null(fight.PlayingAdditional);
        }

        [Fact]
        public void With_those_between_dead_it_plays_twice_in_a_row()
        {
            var (fight, defensor, people) = Fight();
            fight.PlayTwiceARound(defensor.Id, 50);
            ToItsNextTurn(fight, defensor);

            people[0].CurrentHP = 0;
            people[1].CurrentHP = 0;
            Assert.Same(defensor, fight.NextTurn());
            Assert.NotNull(fight.PlayingAdditional);
            Assert.Same(people[2], fight.NextTurn());
        }

        /// <summary>
        /// The timeline's list carries the additional turn behind the one it follows, as the
        /// entry's other kind (f2 { f1 { f1: who, f2: its id } }); the turn's jzc carries the id.
        /// </summary>
        [Fact]
        public void The_timeline_lists_it_as_an_additional_turn()
        {
            var jzu = ProtoMessage.Parse(FightProtocol.BuildTeams(new long[] { -1, 7001, 7002, 7003, 7004 }, -1, 7002, "-1_2"));
            var entries = jzu.Fields.Where(f => f.FieldNumber == 2).Select(f => ProtoMessage.Parse(f.BytesValue)).ToList();
            Assert.Equal(6, entries.Count);
            var extra = entries[3].Fields.Single();
            Assert.Equal(1, extra.FieldNumber);
            var inner = ProtoMessage.Parse(extra.BytesValue);
            Assert.Equal(-1L, inner.Fields.Single(f => f.FieldNumber == 1).VarIntValue);
            Assert.Equal("-1_2", System.Text.Encoding.UTF8.GetString(inner.Fields.Single(f => f.FieldNumber == 2).BytesValue));
            Assert.Equal(3, entries[2].Fields.Single().FieldNumber);

            var jzc = ProtoMessage.Parse(FightProtocol.BuildTurnStart(-1, 290, 2, 2, additional: "-1_2"));
            Assert.Equal("-1_2", System.Text.Encoding.UTF8.GetString(jzc.Fields.Single(f => f.FieldNumber == 3).BytesValue));
            Assert.DoesNotContain(ProtoMessage.Parse(FightProtocol.BuildTurnStart(-1, 290, 2, 2)).Fields, f => f.FieldNumber == 3);
        }

        /// <summary>Its "Sort Initial" grade 2 -- what its companions gone cast -- makes it play twice a round, at 50 %.</summary>
        [Fact]
        public async Task Its_companions_gone_it_plays_twice()
        {
            var (fight, defensor, people) = Fight();
            if (SpellEffects.De(32124, 2).All(e => e.EffectId != 4059)) return;   // no world data
            await FightHandler.AplicarEfectosAsync(null, fight, defensor, 32124, 2, defensor, EffectEngine.AlLanzar, defensor.CellId);
            Assert.Equal(50, fight.TwiceARound[defensor.Id]);
        }
    }
}
