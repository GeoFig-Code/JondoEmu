using System;
using System.Linq;
using System.Threading.Tasks;
using Jondo.Protocol;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.Tests.Economy;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The alignment: Chantal Inea's oath, Morry Gann's way back to neutral, and the side as the
    /// actor, the sheet and the criteria carry it.
    /// </summary>
    public class AlignmentTests
    {
        private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        [Fact]
        public void The_fee_doubles_through_its_ten_steps_and_a_change_waits_two_weeks()
        {
            Assert.Equal(500, Alignments.LeaveCost(0));
            Assert.Equal(1000, Alignments.LeaveCost(1));
            Assert.Equal(256_000, Alignments.LeaveCost(9));
            Assert.Equal(256_000, Alignments.LeaveCost(12));

            var lately = new Alignments.Alignment(Alignments.Bonta, 0, Now - (long)TimeSpan.FromDays(13).TotalMilliseconds);
            var long_ago = lately with { ChangedMs = Now - (long)TimeSpan.FromDays(15).TotalMilliseconds };
            Assert.False(Alignments.MayChange(lately, Now));
            Assert.True(Alignments.MayChange(long_ago, Now));
            Assert.True(Alignments.MayChange(new Alignments.Alignment(Alignments.Neutral, 0, 0), Now));
        }

        [Fact]
        public async Task Chantal_has_one_swear_loyalty_to_a_city()
        {
            const long me = 9_400_300_001;
            Alignments.Set(me, Alignments.Neutral, 0);
            await using var pipe = await ClientPipe.OpenAsync(me, me, 153356294, "Jurador");
            using (SessionContext.Push(pipe.Session))
            {
                var (question, replies) = AlignmentHandler.Opening(AlignmentHandler.Chantal);
                Assert.Equal(36023, question);
                Assert.Equal(new long[] { 47609, 47612, 47616 }, replies);

                (question, replies) = await AlignmentHandler.ReplyAsync(pipe.ToClient, AlignmentHandler.Chantal, 47609);
                Assert.Equal(36024, question);
                Assert.Equal(new long[] { 47611, 47610 }, replies);

                (question, _) = await AlignmentHandler.ReplyAsync(pipe.ToClient, AlignmentHandler.Chantal, 47611);
                Assert.Equal(0, question);
                Assert.Equal(Alignments.Bonta, Alignments.Of(me).Side);
                Assert.Equal(1, Alignments.CriterionSide(me));
                await pipe.Next(Op.Kub);

                Assert.Equal(new long[] { 47615 }, AlignmentHandler.Opening(AlignmentHandler.Chantal).Replies);
            }
            Alignments.Forget(me);
            Assert.Equal(Alignments.Bonta, Alignments.Of(me).Side);   // kept in the base
        }

        [Fact]
        public async Task Morry_Gann_takes_his_fee_and_makes_one_neutral()
        {
            const long me = 9_400_300_002;
            Alignments.Set(me, Alignments.Brakmar, Now - (long)TimeSpan.FromDays(15).TotalMilliseconds);
            await using var pipe = await ClientPipe.OpenAsync(me, me, 95685125, "Arrepentido");
            using (SessionContext.Push(pipe.Session))
            {
                pipe.Session.State.Kamas = 1000;
                var opening = AlignmentHandler.Opening(AlignmentHandler.Morry);
                Assert.Equal(57403, opening.Question);
                Assert.Equal(new long[] { 78119, 78120 }, opening.Replies);

                async Task Expect(long reply, long question, params long[] replies)
                {
                    var next = await AlignmentHandler.ReplyAsync(pipe.ToClient, AlignmentHandler.Morry, reply);
                    Assert.Equal(question, next.Question);
                    Assert.Equal(replies, next.Replies);
                }

                await Expect(78119, 57404, 78117);
                await Expect(78117, 57405, 78114, 78115, 78113);
                await Expect(78115, 57406, 78122, 78116);          // his step, the first: 500 kamas
                await Expect(78122, 57408, 78121);
                await Expect(78114, 57418, 78144, 78142);          // «seguir siendo leal a Brakmar»
                await Expect(78144, 57419, 78171, 78166, 78167);   // the Brakmarian's «Pagar 500 kamas...»
                await Expect(78171, 57433, 78170);                 // «Una firma aquí...»

                Assert.Equal(500, pipe.Session.State.Kamas);
                Assert.Equal(Alignments.Neutral, Alignments.Of(me).Side);
                Assert.Equal(new long[] { 78120 }, AlignmentHandler.Opening(AlignmentHandler.Morry).Replies);

                // Sworn again at once, he has to wait two weeks to leave again.
                Alignments.Set(me, Alignments.Bonta, Now);
                await Expect(78119, 57407, 78118);

                // And without the kamas there is no signing.
                Alignments.Set(me, Alignments.Bonta, 0);
                pipe.Session.State.Kamas = 100;
                await Expect(78144, 57420, 78145);
            }
        }

        [Fact]
        public void The_actor_carries_the_side()
        {
            const long me = 9_400_300_003;
            Alignments.Set(me, Alignments.Brakmar, Now);
            var character = new DatabaseManager.DbCharacter { Id = me, Name = "Negro", Level = 120, Breed = 1 };
            var actor = ProtoMessage.Parse(ConnectionProtocol.BuildPlayerActorBlock(character, 300, 1, 1));
            var body = Sub(Sub(Sub(Sub(actor, 2), 1), 5), 3);
            var identity = Sub(body, 1);
            Assert.Equal(Alignments.Brakmar, identity.Fields.First(f => f.FieldNumber == 2).VarIntValue);
        }

        private static ProtoMessage Sub(ProtoMessage message, int field)
            => ProtoMessage.Parse(message.Fields.First(f => f.FieldNumber == field && f.WireType == 2).BytesValue);
    }
}
