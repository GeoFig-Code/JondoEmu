using System;
using System.IO;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.World.Fights;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The alliances' prisms: the map actor against «Bloodbath»'s prism in «Chats/hablar por canal
    /// Comunidad», the rules of their fight, and the store, one per subarea.
    /// </summary>
    [Collection("alliances")]
    public class PrismTests : IDisposable
    {
        private readonly string _file;

        public PrismTests()
        {
            _file = Path.Combine(Path.GetTempPath(), $"jondo-prisma-{Guid.NewGuid():N}.db");
            AllianceStore.ConnectionStringOverride = $"Data Source={_file}";
            PrismStore.ConnectionStringOverride = $"Data Source={_file}";
        }

        public void Dispose()
        {
            PrismStore.ConnectionStringOverride = null;
            AllianceStore.ConnectionStringOverride = null;
            TestDatabases.ReleasePool(_file);
            try { File.Delete(_file); } catch (IOException) { }
        }

        private static byte[] Hex(string hex) => Convert.FromHexString(hex);

        private static readonly AllianceStore.Alliance Bloodbath = new()
        {
            Uuid = "01ba8c1c-63e6-4c3b-95e5-a54c3eaddc3a",
            Name = "Bloodbath",
            Tag = "BLOOD",
            EmblemSymbol = 104,
            EmblemSymbolColor = 8,
            EmblemSymbolRgb = 11862276,
        };

        /// <summary>The prism monster's look, MonsterTemplates 3451.</summary>
        private const string PrismMonsterLook = "{2211||1=12772846,2=16752644,3=14301485,4=12148110,5=3994880}";

        /// <summary>
        /// The map actor: kind 2 with when it was placed and its alliance, at its cell. Byte for
        /// byte with the capture's look, skin included.
        /// </summary>
        [Fact]
        public void The_map_actor_matches_the_capture()
        {
            var prism = new PrismStore.Prism
            {
                Id = PrismStore.ContextBase + 20000,
                CellId = 524,
                Direction = 1,
                PlacedUtcMs = 1784886505835,
            };
            Assert.Equal(-20000, prism.ContextId);
            Assert.Equal(Hex("0a05088c041001126f0a54125210ebe29a9cf93320e9e98cd3062a43120908681008288482d4051a09426c6f6f" +
                             "6462617468222430316261386331632d363365362d346333622d393565352d6135346333656164646333612a05" +
                             "424c4f4f441a170a0ceecb8b0e808080488482d455100318a3113202f11418e0e3feffffffffffff01"),
                         PrismProtocol.Actor(prism, Bloodbath, Hex("0a0ceecb8b0e808080488482d455100318a3113202f114")).Build());
        }

        /// <summary>
        /// Its look: the prism monster's bones and first colour, the symbol's prism skin (2673 for
        /// symbol 104, matched by texture), and the emblem's colours as 9 and 10 -- the capture's.
        /// </summary>
        [Fact]
        public void Its_look_is_the_capture_s()
        {
            if (AllianceData.PrismSkinOfSymbol(104) == 0) return;   // no alliance data

            Assert.Equal("{2211|2673|1=12772846,9=0,10=11862276}", PrismProtocol.LookString(Bloodbath, PrismMonsterLook));
            Assert.Equal(4793, AllianceData.PrismSkinOfSymbol(434));
            Assert.Equal(Hex("0a0ceecb8b0e808080488482d455100318a3113202f114"),
                         ConnectionProtocol.MonsterLook(PrismProtocol.LookString(Bloodbath, PrismMonsterLook), hardcore: false).Build());
        }

        /// <summary>Its fight: the kam's 9, the usual clock, no challenges and no loot.</summary>
        [Fact]
        public void Its_fight_rules()
        {
            var rules = FightRules.ContraPrisma;
            Assert.Equal(9, rules.TipoDelKam);
            Assert.Equal(450, rules.RelojDeColocacion);
            Assert.False(rules.HayRetos);
            Assert.False(rules.ReparteBotin);
            Assert.True(rules.EnfrenteHayMonstruos);
        }

        /// <summary>One per subarea, found by its subarea, its map and its actor.</summary>
        [Fact]
        public void One_holds_a_subarea()
        {
            var prism = PrismStore.Place(3, 838, 144445702, 300, 1);
            Assert.NotNull(prism);
            Assert.Null(PrismStore.Place(4, 838, 144445703, 300, 1));

            Assert.Same(prism, PrismStore.InSubArea(838));
            Assert.Same(prism, PrismStore.OnMap(144445702));
            Assert.Same(prism, PrismStore.ByContextId(prism.ContextId));
            Assert.Single(PrismStore.OfAlliance(3));

            PrismStore.Remove(prism);
            Assert.Null(PrismStore.InSubArea(838));
            Assert.NotNull(PrismStore.Place(4, 838, 144445703, 300, 1));
        }

        /// <summary>
        /// «Speak» on a prism (iov 3 on its actor): the dialogue opens (ioc) with the prism's
        /// question (iot, empty), and its «Leave» then closes it.
        /// </summary>
        [Fact]
        public async System.Threading.Tasks.Task Speaking_to_it_opens_its_dialogue()
        {
            var alliance = AllianceStore.Create(5, "Habladores", "HAB", 1, 1, 1, 1, 1);
            var prism = PrismStore.Place(alliance.Id, 839, 144445704, 300, 1);
            try
            {
                await using var pipe = await Economy.ClientPipe.OpenAsync(990000022, 990000023, 144445704);
                using (SessionContext.Push(pipe.Session))
                {
                    Assert.False(await PrismHandler.TalkAsync(pipe.ToClient, 144445704, prism.ContextId - 1));
                    Assert.True(await PrismHandler.TalkAsync(pipe.ToClient, 144445704, prism.ContextId));
                    Assert.Equal(ConnectionProtocol.BuildNpcDialog(144445704, prism.ContextId), await pipe.Next(Jondo.Unity.Protocol.Op.Ioc));
                    Assert.Empty(await pipe.Next(Jondo.Unity.Protocol.Op.Iot));
                    Assert.Equal(prism.ContextId, pipe.Session.State.OpenActorDialogue);

                    Assert.True(await TaxCollectorHandler.CloseTalkAsync(pipe.ToClient));
                    await pipe.Next(Jondo.Unity.Protocol.Op.Kld);
                }
            }
            finally { PrismStore.Remove(prism); }
        }

        /// <summary>The prism item's criterion: the right to place a prism and the subarea's level.</summary>
        [Fact]
        public void The_prism_asks_for_the_right()
        {
            var alliance = AllianceStore.Create(1, "Prismas", "PRI", 1, 1, 1, 1, 1);
            AllianceStore.Join(2, alliance.Id, AllianceStore.RankNewcomer);

            Assert.True(PrismHandler.CriterionHolds("Ox=4", 1, 1));
            Assert.False(PrismHandler.CriterionHolds("Ox=4", 2, 1));
        }
    }
}
