using System.Linq;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The breeding window: the six paddocks and their gates as the client's data places them, and
    /// the messages that open the window, read off the client (eqa sends, eqc handles).
    /// </summary>
    [Collection("MapManager")]
    public class BreedingTests
    {
        /// <summary>
        /// Six paddocks, «Cercado de principiante» to «Cercado de maestro», opened at the breeder's
        /// level 1, 40, 80, 120, 160 and 200 by the skills 423 to 428, each with its gate on its map.
        /// </summary>
        [Fact]
        public void Six_paddocks_each_with_its_gate_skill_and_level()
        {
            if (Breeding.Paddocks.Count == 0) return;

            Assert.Equal(6, Breeding.Paddocks.Count);
            Assert.Equal(new[] { 1, 40, 80, 120, 160, 200 }, Breeding.Paddocks.Select(p => p.MinLevel));
            Assert.Equal(Enumerable.Range(423, 6), Breeding.Paddocks.Select(p => p.Skill));
            Assert.All(Breeding.Paddocks, p => Assert.True(p.MapId > 0 && p.Element > 0));
            Assert.Equal("Cercado de principiante", Breeding.PaddockOf(1)!.Name);
            Assert.Equal(120, Breeding.GateType);
            Assert.Equal(6, Breeding.Gauges.Count);
        }

        /// <summary>A beginner breeder opens the first paddock, and only the master opens the last.</summary>
        [Fact]
        public void The_breeder_level_opens_the_paddocks()
        {
            if (Breeding.Paddocks.Count == 0) return;

            Assert.True(Breeding.Opens(Breeding.PaddockOf(1)!, 1));
            Assert.False(Breeding.Opens(Breeding.PaddockOf(2)!, 39));
            Assert.True(Breeding.Opens(Breeding.PaddockOf(2)!, 40));
            Assert.False(Breeding.Opens(Breeding.PaddockOf(6)!, 199));
        }

        /// <summary>Each gate is declared on its map with the skill of its paddock.</summary>
        [Fact]
        public void Each_gate_opens_its_paddock()
        {
            if (Breeding.Paddocks.Count == 0) return;
            Interactives.Initialize();
            var first = Breeding.PaddockOf(1)!;
            if (Interactives.ElementsOf(first.MapId).Count == 0) return;   // no world data
            InteractiveRegistry.Initialize();

            foreach (var paddock in Breeding.Paddocks)
            {
                var gate = Assert.Single(InteractiveRegistry.OnMap(paddock.MapId), i => i.Element.Id == paddock.Element);
                Assert.Equal(Breeding.GateType, gate.Type);
                var action = Assert.Single(gate.Actions);
                Assert.Equal(InteractiveActionKind.PaddockGate, action.Kind);
                Assert.Equal(paddock.Skill, action.SkillId);
            }
        }

        /// <summary>
        /// hsb opens the window on its paddock; hrc carries the paddock's content in its result and
        /// the paddock; hus every paddock with whether it opens.
        /// </summary>
        [Fact]
        public void The_window_messages()
        {
            Assert.Equal(new byte[] { 0x08, 0x02 }, BreedingProtocol.BuildOpenWindow(2));

            var content = BreedingProtocol.PaddockContent(new int[BreedingProtocol.GaugeCount], new[] { 3 });
            var hrc = ProtoMessage.Parse(BreedingProtocol.BuildPaddock(4, content)).Fields;
            Assert.Equal(4, (int)hrc.Single(f => f.FieldNumber == 3).VarIntValue);
            var result = ProtoMessage.Parse(hrc.Single(f => f.FieldNumber == 1).BytesValue).Fields;
            var paddock = ProtoMessage.Parse(result.Single(f => f.FieldNumber == 2).BytesValue).Fields;
            Assert.Equal(BreedingProtocol.GaugeCount, paddock.Count(f => f.FieldNumber == 1));
            Assert.Equal(3, (int)paddock.Single(f => f.FieldNumber == 2).VarIntValue);

            var hus = ProtoMessage.Parse(BreedingProtocol.BuildAccessiblePaddocks(new[] { (1, true), (2, false) })).Fields;
            Assert.Equal(2, hus.Count(f => f.FieldNumber == 1));
        }
    }
}
