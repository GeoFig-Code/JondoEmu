using System;
using System.Collections.Generic;
using System.Linq;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The Travelers' Tower and the divine dimensions' portals: the captures «entrar y salir de
    /// dimensiones-usando creador de portales de la torre de viajeros» and «salir de anutropia por
    /// portal-navegar por salas de torre de los viajeros-lanzar busqueda portal».
    /// </summary>
    [Collection("MapManager")]
    public class TravelersTowerTests
    {
        public TravelersTowerTests() => TravelersTower.Reset();

        private static string Hex(byte[] bytes) => string.Concat(bytes.Select(b => b.ToString("x2")));

        /// <summary>The capture's character: 763, 764 and 766, the mechanism's 346, 347 and 349.</summary>
        private static bool Captured(long character, int alteration) => alteration is 763 or 764 or 766;

        /// <summary>
        /// Every room's teleporter offers the seven rooms, the mechanism its six names under skill
        /// 184, the portal its way through, each dimension's portal back its two skills -- each skill
        /// with an instance of its own on its map.
        /// </summary>
        [Fact]
        public void The_tower_is_declared_as_the_captures_draw_it()
        {
            Interactives.Initialize();
            InteractiveRegistry.Initialize();
            if (InteractiveRegistry.OnMap(TravelersTower.PortalRoom).Count == 0) return;   // no client data

            foreach (var room in TravelersTower.Rooms)
            {
                var teleporter = Assert.Single(InteractiveRegistry.OnMap(room.MapId), i => i.Element.Id == room.Teleporter);
                Assert.Equal(TravelersTower.TeleporterType, teleporter.Type);
                Assert.Equal(new[] { 456, 454, 453, 452, 451, 450, 449 }, teleporter.Actions.Select(a => a.SkillId));
                Assert.All(teleporter.Actions, a => Assert.Equal(InteractiveActionKind.TowerRoom, a.Kind));
            }

            var portalRoom = InteractiveRegistry.OnMap(TravelersTower.PortalRoom);
            var creator = Assert.Single(portalRoom, i => i.Element.Id == TravelersTower.Creator);
            Assert.Equal(new[] { 359, 350, 349, 348, 347, 346 }, creator.Actions.Select(a => a.SkillNameId));
            Assert.All(creator.Actions, a => Assert.Equal(TravelersTower.UseSkill, a.SkillId));
            var portal = Assert.Single(portalRoom, i => i.Element.Id == TravelersTower.Portal);
            Assert.Equal(TravelersTower.PortalType, portal.Type);
            var instances = portalRoom.SelectMany(i => i.Actions).Select(a => a.SkillInstanceId).ToList();
            Assert.Equal(instances.Count, instances.Distinct().Count());

            foreach (var (mapId, element) in TravelersTower.WaysBack)
            {
                var back = Assert.Single(InteractiveRegistry.OnMap(mapId), i => i.Element.Id == element);
                Assert.Equal(new[] { 259, 258 }, back.Actions.Select(a => a.SkillId));
            }
            foreach (var dimension in TravelersTower.Dimensions)
                Assert.Contains(TravelersTower.WaysBack, w => w.MapId == dimension.EntryMap);
        }

        /// <summary>
        /// The mechanism offers «Cerrar el portal» and the dimensions whose alteration one has --
        /// the capture's 359, 349, 347, 346 --; the portal lets through only to one of them.
        /// </summary>
        [Fact]
        public void Each_one_is_offered_his_own_dimensions()
        {
            Assert.Equal(new[] { 359, 349, 347, 346 }, TravelersTower.CreatorNamesFor(1, Captured));
            Assert.Equal(new[] { 359 }, TravelersTower.CreatorNamesFor(2, (c, a) => false));

            Assert.False(TravelersTower.LetsThrough(1, Captured));
            TravelersTower.Switch(TravelersTower.DimensionOf(349));
            Assert.True(TravelersTower.LetsThrough(1, Captured));
            Assert.False(TravelersTower.LetsThrough(2, (c, a) => false));
            TravelersTower.Switch(TravelersTower.DimensionOf(348));
            Assert.False(TravelersTower.LetsThrough(1, Captured));
        }

        /// <summary>
        /// The stagings as the capture plays them: the mechanism (1), the open portal closing (3),
        /// the new one (4 Anutropía, 5 Sramvil, 7 Zurcalia); closing is 1 and 3.
        /// </summary>
        [Fact]
        public void The_stagings_follow_the_capture()
        {
            var anutropia = TravelersTower.DimensionOf(346);
            var sramvil = TravelersTower.DimensionOf(347);
            var zurcalia = TravelersTower.DimensionOf(349);
            Assert.Equal(new[] { 1, 4 }, TravelersTower.Switch(anutropia));
            Assert.Equal(new[] { 1, 3, 5 }, TravelersTower.Switch(sramvil));
            Assert.Equal(new[] { 1, 3, 7 }, TravelersTower.Switch(zurcalia));
            Assert.Equal(new[] { 1, 3 }, TravelersTower.Switch(null));
            Assert.Null(TravelersTower.Open);
            Assert.Equal(new[] { 1, 7 }, TravelersTower.Switch(zurcalia));
            Assert.Equal(new[] { 6, 8 }, new[] { TravelersTower.DimensionOf(348).Staging, TravelersTower.DimensionOf(350).Staging });
        }

        /// <summary>
        /// The elements as the capture declares them, its instances apart: the mechanism with its
        /// four names and type -1, the portal open with its way through and type 232, and closed
        /// with none; the room's own teleporter puts back on cell 522.
        /// </summary>
        [Fact]
        public void The_messages_go_as_the_capture_has_them()
        {
            Assert.Equal("0884f8a980a919108a04", Hex(TravelersTowerHandler.BuildPutBack(870000000004, 522)));
            Assert.Equal("0864106418" + "04", Hex(GuildRaidStagings.BuildPlay(4, GuildRaidStagings.AtEnd)));

            Interactives.Initialize();
            InteractiveRegistry.Initialize();
            var portalRoom = InteractiveRegistry.OnMap(TravelersTower.PortalRoom);
            if (portalRoom.Count == 0) return;   // no client data
            var creator = portalRoom.Single(i => i.Element.Id == TravelersTower.Creator);
            var portal = portalRoom.Single(i => i.Element.Id == TravelersTower.Portal);

            // The capture's: f3 { f1 1, f4 {7757, 184, 359}, {7755, 184, 349}, {7753, 184, 347}, {7752, 184, 346}, f5 539177, f6 -1 }.
            var seen = TravelersTower.CreatorNamesFor(1, Captured);
            var declared = creator.Actions.Where(a => seen.Contains(a.SkillNameId)).ToList();
            Assert.Equal(new[] { 359, 349, 347, 346 }, declared.Select(a => a.SkillNameId));

            // Closed: the capture's «1a09 0801 28a8f420 30e801» -- no skill.
            var closed = ProtoMessage.Parse(ProtoMessage.Parse(TravelersTowerHandler.BuildElementFor(portal, 1)).Fields.Single().BytesValue);
            Assert.Equal(new[] { 1, 5, 6 }, closed.Fields.Select(f => f.FieldNumber));
            Assert.Equal(TravelersTower.Portal, closed.Fields[1].VarIntValue);
            Assert.Equal(232, closed.Fields[2].VarIntValue);
        }
    }
}
