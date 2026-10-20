using System;
using System.Collections.Generic;
using System.Linq;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The divine dimensions' portals in the world, as the 3.6.10.11 client reads them: map actor
    /// kind 3, the ioc that names the portal's type, its NPC's sentence, the hhu that crosses it.
    /// </summary>
    [Collection("MapManager")]
    public class DimensionPortalTests
    {
        private static DimensionPortals.Portal Anutropia(long mapId = 88081171, int cell = 300)
        {
            var portal = new DimensionPortals.Portal { Dimension = TravelersTower.Dimensions[0] };
            DimensionPortals.Open(portal, mapId, cell, 1_000_000);
            return portal;
        }

        [Fact]
        public void Four_dimensions_have_a_portal_in_the_world()
        {
            Assert.Equal(new[] { (1, 2374), (2, 2594), (3, 2667), (4, 3136) },
                DimensionPortals.WithPortal.Select(d => (d.PortalType, d.PortalNpc)));
            Assert.DoesNotContain(DimensionPortals.WithPortal, d => d.Name == "Osavora");
        }

        /// <summary>JOL's numbers: 128 crossings, and a day unused before it closes; each crossing starts the day again.</summary>
        [Fact]
        public void A_portal_closes_spent_or_left_unused()
        {
            var portal = Anutropia();
            Assert.Equal(128, portal.UsesLeft);
            Assert.Equal(1_000_000 + 86_400_000L, portal.ClosesMs);
            Assert.False(DimensionPortals.Expired(portal, 1_000_000 + 86_399_999L));
            Assert.True(DimensionPortals.Expired(portal, 1_000_000 + 86_400_000L));

            Assert.False(DimensionPortals.Cross(portal, 2_000_000));
            Assert.Equal(127, portal.UsesLeft);
            Assert.Equal(2_000_000 + 86_400_000L, portal.ClosesMs);
            portal.UsesLeft = 1;
            Assert.True(DimensionPortals.Cross(portal, 3_000_000));
            Assert.Equal(0, portal.UsesLeft);
        }

        [Fact]
        public void A_portal_opens_where_no_other_stands()
        {
            var grid = new TreasureHunts.Grid(new[] { (1L, 0, 0), (2L, 1, 0), (3L, 2, 0) });
            var dice = new Random(4);
            for (int i = 0; i < 20; i++)
            {
                var place = DimensionPortals.Place(grid, new[] { 1L, 2L }, m => new[] { 300, 301 }, dice);
                Assert.Equal(3L, place!.Value.MapId);
                Assert.Contains(place.Value.Cell, new[] { 300, 301 });
            }
            Assert.Null(DimensionPortals.Place(grid, new[] { 1L, 2L, 3L }, m => new[] { 300 }, dice));
            Assert.Null(DimensionPortals.Place(grid, Array.Empty<long>(), m => Array.Empty<int>(), dice));
        }

        /// <summary>
        /// The actor as gja reads it -- kind 3, f2 the portal and f3 the area --; the ioc with the
        /// portal's type in f2, as epd hands it to fsw; the hhu's portal in f3, as eoy writes it.
        /// </summary>
        [Fact]
        public void The_messages_go_as_the_client_reads_them()
        {
            var portal = Anutropia(88081171, 300);
            byte[] look = ConnectionProtocol.BuildNpcLook(2720, Array.Empty<long>(), Array.Empty<long>(), Array.Empty<long>());
            var actor = ProtoMessage.Parse(DimensionPortalProtocol.Actor(portal, 53, look).Build());
            Assert.Equal(new[] { 1, 2, 3 }, actor.Fields.Select(f => f.FieldNumber));
            Assert.Equal(DimensionPortals.ContextBase - 1, actor.Fields[2].VarIntValue);
            Assert.Equal(300, ProtoMessage.Parse(actor.Fields[0].BytesValue).Fields.Single().VarIntValue);
            var shape = ProtoMessage.Parse(actor.Fields[1].BytesValue);
            Assert.Equal(look, shape.Fields.Single(f => f.FieldNumber == 3).BytesValue);
            var kind = ProtoMessage.Parse(shape.Fields.Single(f => f.FieldNumber == 1).BytesValue).Fields.Single();
            Assert.Equal(DimensionPortals.ActorKind, kind.FieldNumber);
            Assert.Equal(new[] { (2, 1L), (3, 53L) }, ProtoMessage.Parse(kind.BytesValue).Fields.Select(f => (f.FieldNumber, f.VarIntValue)));

            var ioc = ProtoMessage.Parse(DimensionPortalProtocol.BuildDialog(portal));
            Assert.Equal(new[] { (2, 1L), (4, 88081171L), (5, DimensionPortals.ContextBase - 1) },
                ioc.Fields.Select(f => (f.FieldNumber, f.VarIntValue)));

            Assert.Equal(3, DimensionPortalProtocol.ReadUse(Pb.New().Var(3, 3).Build()));
        }

        /// <summary>The sentence's #1 the crossings left, its $time#2 the closing time in unix seconds.</summary>
        [Fact]
        public void The_portal_says_its_uses_and_when_it_closes()
        {
            var portal = Anutropia();
            var ios = ProtoMessage.Parse(DimensionPortalProtocol.BuildQuestion(portal, 17474, new[] { 21290L }));
            Assert.Equal(17474, ios.Fields.First(f => f.FieldNumber == 1).VarIntValue);
            Assert.Equal(21290, ProtoMessage.Parse(ios.Fields.Single(f => f.FieldNumber == 2).BytesValue).Fields[0].VarIntValue);
            var parameters = ios.Fields.Where(f => f.FieldNumber == 3).Select(f => System.Text.Encoding.UTF8.GetString(f.BytesValue)).ToList();
            Assert.Equal(new[] { "128", ((1_000_000 + 86_400_000L) / 1000).ToString() }, parameters);
        }

        /// <summary>The portals' NPCs as the client's data has them: their look, sentence and reply.</summary>
        [Fact]
        public void The_portals_are_their_npcs()
        {
            var expected = new Dictionary<int, (long Bones, long Message, long Reply)>
            {
                [2374] = (2720, 17474, 21290),
                [2594] = (2893, 18120, 21726),
                [2667] = (3031, 19086, 22756),
                [3136] = (3489, 22467, 27064),
            };
            foreach (var (npc, (bones, message, reply)) in expected)
            {
                var template = Npcs.EnsureTemplate(npc);
                if (template == null) return;   // no world data
                Assert.Equal(bones, Npcs.Variantes(template.Look)[0].Bones);
                Assert.Equal(message, template.DialogMessageId);
                Assert.Equal(new[] { reply }, template.Replies);
            }
        }
    }
}
