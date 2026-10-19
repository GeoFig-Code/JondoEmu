using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Jondo.Unity.Launcher;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.Protocol;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// Alliances: the frames against the one capture that has one, "entrar en alianza y salir de
    /// alianza" -- a character joining «A B S I N T O» and leaving it --, and the store underneath.
    /// </summary>
    /// <remarks>
    /// The store points at a temp database for the whole class; its tests share one collection
    /// so no other alliance test moves that pointer under them.
    /// </remarks>
    [Collection("alliances")]
    public class AllianceTests : IDisposable
    {
        private readonly string _file;

        public AllianceTests()
        {
            _file = Path.Combine(Path.GetTempPath(), $"jondo-alianza-{Guid.NewGuid():N}.db");
            AllianceStore.ConnectionStringOverride = $"Data Source={_file}";
        }

        public void Dispose()
        {
            AllianceStore.ConnectionStringOverride = null;
            TestDatabases.ReleasePool(_file);
            try { File.Delete(_file); } catch (IOException) { }
        }

        /// <summary>«A B S I N T O» as the capture shows it.</summary>
        private static readonly AllianceStore.Alliance Absinto = new()
        {
            Uuid = "76715ab4-8f9b-46d6-9227-db680514382d",
            Name = "A B S I N T O",
            Tag = "ABS",
            EmblemSymbol = 408,
            EmblemSymbolColor = 27,
            EmblemBackground = 3116544,
            EmblemSymbolRgb = 15859463,
            CreatedUtcS = 1774821557,
        };

        private static byte[] Hex(string hex) => Convert.FromHexString(hex);

        // ─── The frames, byte for byte against the capture ───────────────────────

        /// <summary>Joining (mca) and belonging (mbz) carry the alliance block exactly as the real server does.</summary>
        [Fact]
        public void Joining_and_belonging_match_the_capture()
        {
            const string block = "120f089803101b18809cbe012887fec7071a0d41204220532049204e2054204f2224" +
                                 "37363731356162342d386639622d343664362d393232372d6462363830353134333832642a03414253";
            Assert.Equal(Hex("0804124b" + block), AllianceProtocol.BuildJoined(Absinto, 4));
            Assert.Equal(Hex("10041a4b" + block), AllianceProtocol.BuildMembership(Absinto, 4));
        }

        /// <summary>The four ranks of «A B S I N T O» (mbm), with their own names, rights and icons.</summary>
        [Fact]
        public void The_ranks_match_the_capture()
        {
            var ranks = new List<AllianceStore.Rank>
            {
                new() { Id = 1, Name = "JAEGER MIESTER", Icon = 116, Order = 0,
                        Rights = new() { 1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12, 13, 15, 16, 17, 18, 19, 21 } },
                new() { Id = 2, Name = "Pingola Master", Flag = true, Icon = 23, Order = 1,
                        Rights = new() { 1, 2, 3, 4, 6, 7, 8, 9, 10, 12, 13, 15, 16, 17, 18, 21 } },
                new() { Id = 3, Name = "Bebado Pingusso", Flag = true, Icon = 45, Order = 2,
                        Rights = new() { 18, 9, 10, 12 } },
                new() { Id = 4, Name = "Vira Lata", Icon = 117, Order = 3 },
            };

            Assert.Equal(Hex(
                "0a2c120e4a4145474552204d4945535445521a141a1201020304060708090a0b0c0d0f10111213152202107428010a2e120e50696e676f6c61204d61737465721a1408011a1001020304060708090a0c0d0f1011121522041017180128020a23120f42656261646f2050696e677573736f1a0808011a0412090a0c2204102d180228030a15120956697261204c6174611a002204107518032804"),
                AllianceProtocol.BuildRanks(ranks));
        }

        /// <summary>The alliance message and the bulletin on joining: only the alliance's founding date.</summary>
        [Fact]
        public void The_notices_match_the_capture()
        {
            Assert.Equal(Hex("0a0610b5c1a6ce06"), AllianceProtocol.BuildNotice(new AllianceStore.Notice("", 1774821557, "", 0)));
            Assert.Equal(Hex("0862"), AllianceProtocol.BuildEmote());
        }

        /// <summary>
        /// The sheet (mcx): the alliance with its card -- the recruitment card, the founding date and
        /// the 46 members --, then a line per member. The first two of the capture, byte for byte.
        /// </summary>
        [Fact]
        public void The_sheet_matches_the_capture()
        {
            var profile = new AllianceStore.Profile
            {
                Recruitment = 1, MinLevel = 200, Description = "ABSINTO = DEATH", Title = "ABSINTO",
                WhenMs = 1774821658515,
                Tags = new() { 4, 5, 7, 8, 9, 10, 11, 17, 19, 20, 21, 22, 23, 24, 25, 26 },
                Languages = new() { 8, 7, 0, 4, 5, 9, 3, 6, 2, 1 },
            };
            var card = AllianceProtocol.Card(Absinto, profile, "Kyuu-Lord", 46);
            var rows = new[]
            {
                new AllianceProtocol.MemberRow(
                    new AllianceStore.Member { CharacterId = 124446834978, Rank = 4, JoinedUtcMs = 1779463581262 },
                    "Zob-Bilher", 290, 86536171, 14, 1, 1893, false),
                new AllianceProtocol.MemberRow(
                    new AllianceStore.Member { CharacterId = 140435849506, Rank = 4, JoinedUtcMs = 1782776697319 },
                    "Mascu-okres", 224, 35125886, 9, 0, 759, false),
            };

            Assert.Equal(Hex(
                "1acf01120f089803101b18809cbe012887fec7071a0d41204220532049204e2054204f222437363731356162342d386639622d343664362d393232372d6462363830353134333832642a034142533281010a770a094b7975752d4c6f7264100120c8012a0f414253494e544f203d204445415448321004050708090a0b11131415161718191a4a07414253494e544f50939ff5dcd3335801622437363731356162342d386639622d343664362d393232372d6462363830353134333832646a0a0807000405090306020110b5c1a6ce06282e" +
                "224910ffffffffffffffffff011a3c0a33120a5a6f622d42696c68657218a2022a2208e50e100420cedcad82e53328ffffffffffffffffff01300e4200480150ebdfa12910a282f0cccf03" +
                "224810ffffffffffffffffff011a3b0a32120b4d617363752d6f6b72657318e0012a2008f705100420e7a396aef13328ffffffffffffffffff013009420050fef4df1010a28284958b04"),
                AllianceProtocol.BuildInformation(Absinto, card, rows));
        }

        /// <summary>The requests are read where the capture writes them: the uuid in mac, the character in mcc.</summary>
        [Fact]
        public void The_opcodes_are_the_captures()
        {
            Assert.Equal("mac", Op.Mac);
            Assert.Equal("mcc", Op.Mcc);
            Assert.Equal("mde", Op.Mde);
            Assert.Equal("mcx", Op.Mcx);
            Assert.Equal("mbt", Op.Mbt);
            Assert.Equal("mcw", Op.Mcw);
            Assert.Equal("mdk", Op.Mdk);
        }

        /// <summary>The temple's blue door is where the capture pressed it: 481519, cell 186 of 115083777.</summary>
        [Fact]
        public void The_temple_door_is_where_the_capture_pressed_it()
        {
            Assert.Equal(184, AllianceHandler.TempleDoorSkill);
            if (!File.Exists(Paths.InteractiveElementsJson)) return;

            using var doc = JsonDocument.Parse(File.ReadAllText(Paths.InteractiveElementsJson));
            var door = doc.RootElement.GetProperty(AllianceHandler.TempleMap.ToString()).EnumerateArray()
                .Single(e => e.GetProperty("e").GetInt32() == AllianceHandler.TempleDoor);
            Assert.Equal(186, door.GetProperty("c").GetInt32());
        }

        // ─── The store ───────────────────────────────────────────────────────────

        /// <summary>
        /// A founded alliance has its founder as leader with every right, and the four ranks of the
        /// client's data in its own words.
        /// </summary>
        [Fact]
        public void Founding_makes_the_founder_its_leader()
        {
            var alliance = AllianceStore.Create(8101, "Jondo Unidos", "JON", 408, 27, 3116544, 0, 15859463);

            Assert.Equal(alliance.Id, AllianceStore.AllianceOf(8101)?.Id);
            Assert.Equal(AllianceStore.RankLeader, AllianceStore.RankOf(8101));
            Assert.True(Guid.TryParse(alliance.Uuid, out _));
            Assert.Equal(alliance.Id, AllianceStore.ByUuid(alliance.Uuid)?.Id);
            Assert.Equal(alliance.Id, AllianceStore.ByName("jondo unidos")?.Id);
            Assert.Equal(alliance.Id, AllianceStore.ByTag("jon")?.Id);

            var ranks = AllianceStore.Ranks(alliance.Id);
            Assert.Equal(new[] { 1, 2, 3, 4 }, ranks.Select(r => r.Id));
            Assert.Equal(new[] { 116, 115, 114, 117 }, ranks.Select(r => r.Icon));
            Assert.Equal("alliance.rank.1.name", ranks[0].Name);
            Assert.Equal("alliance.rank.4.name", ranks[3].Name);
            if (AllianceData.Rights.Count > 0)
            {
                Assert.Equal(AllianceData.Rights.Count, ranks[0].Rights.Count);
                Assert.Empty(ranks[3].Rights);
            }
            Assert.True(AllianceStore.HasRight(8101, AllianceData.Expel));
        }

        /// <summary>
        /// Joining comes in as «Bisoño», without rights. A leader who leaves hands the alliance on to
        /// the next in line; the last one out dissolves it.
        /// </summary>
        [Fact]
        public void Leaving_hands_on_the_lead_and_the_last_one_dissolves_it()
        {
            var alliance = AllianceStore.Create(8201, "Los Del Templo", "TEM", 1, 2, 3, 0, 4);
            AllianceStore.Join(8202, alliance.Id);
            Assert.Equal(AllianceStore.RankNewcomer, AllianceStore.RankOf(8202));
            Assert.False(AllianceStore.HasRight(8202, AllianceData.Expel));

            var departure = AllianceStore.Leave(8201);
            Assert.False(departure.Dissolved);
            Assert.Equal(8202, departure.NewLeader);
            Assert.Equal(AllianceStore.RankLeader, AllianceStore.RankOf(8202));

            departure = AllianceStore.Leave(8202);
            Assert.True(departure.Dissolved);
            Assert.Null(AllianceStore.ByUuid(alliance.Uuid));
            Assert.Null(AllianceStore.Leave(8202));
        }

        // ─── Founding's rules ────────────────────────────────────────────────────

        [Theory]
        [InlineData("A B S I N T O", true)]
        [InlineData("Jondo Unidos", true)]
        [InlineData("L'Alliance", true)]
        [InlineData("Ab", false)]
        [InlineData("-Jondo", false)]
        [InlineData("Jon--do", false)]
        [InlineData("Jooondo", false)]
        [InlineData("Jondo2", false)]
        public void The_name_follows_the_editors_rules(string name, bool valid)
            => Assert.Equal(valid, AllianceHandler.IsValidName(name));

        [Theory]
        [InlineData("ABS", true)]
        [InlineData("BLOOD", true)]
        [InlineData("AB", false)]
        [InlineData("SIXSIX", false)]
        [InlineData("AB1", false)]
        public void The_tag_is_three_to_five_letters(string tag, bool valid)
            => Assert.Equal(valid, AllianceHandler.IsValidTag(tag));

        /// <summary>
        /// Founding answers what the editor knows how to say, before it touches the bag: a member
        /// already in an alliance, a bad name or tag, a name or tag already taken.
        /// </summary>
        [Fact]
        public async Task Founding_refuses_what_the_editor_says_it_refuses()
        {
            AllianceStore.Create(8301, "Ya Existe", "YAE", 1, 2, 3, 0, 4);

            Assert.Equal(AllianceProtocol.CreationAlreadyInAlliance,
                await AllianceHandler.FoundAsync(null, 8301, "Otra Mas", "OTR", 1, 2, 3, 0, 4));
            Assert.Equal(AllianceProtocol.CreationInvalidName,
                await AllianceHandler.FoundAsync(null, 8302, "X", "OTR", 1, 2, 3, 0, 4));
            Assert.Equal(AllianceProtocol.CreationInvalidTag,
                await AllianceHandler.FoundAsync(null, 8302, "Otra Mas", "O", 1, 2, 3, 0, 4));
            Assert.Equal(AllianceProtocol.CreationNameUsed,
                await AllianceHandler.FoundAsync(null, 8302, "ya existe", "OTR", 1, 2, 3, 0, 4));
            Assert.Equal(AllianceProtocol.CreationTagUsed,
                await AllianceHandler.FoundAsync(null, 8302, "Otra Mas", "yae", 1, 2, 3, 0, 4));
        }

        // ─── The map ─────────────────────────────────────────────────────────────

        /// <summary>
        /// The actor of whoever has an alliance carries it as an option f5 { f5 { f2 alliance } },
        /// as the jsn of the capture redraws the character on joining; one without carries none.
        /// </summary>
        [Fact]
        public void An_allied_actor_carries_its_alliance()
        {
            var alliance = AllianceStore.Create(8401, "Jondo Unidos", "JON", 408, 27, 3116544, 0, 15859463);
            var member = new DatabaseManager.DbCharacter { Id = 8401, Name = "Hiper-master", Level = 200, Breed = 2 };
            var loner = new DatabaseManager.DbCharacter { Id = 8402, Name = "Solo", Level = 200, Breed = 2 };

            var option = AllianceOption(ConnectionProtocol.BuildPlayerActorBlock(member, 341, 5, 65924386));
            Assert.NotNull(option);
            Assert.Equal(AllianceProtocol.Block(alliance).Build(), option);
            Assert.Null(AllianceOption(ConnectionProtocol.BuildPlayerActorBlock(loner, 341, 5, 65924386)));
        }

        /// <summary>
        /// A connected member is in the world, the details' f3 = 1, which is what the members tab
        /// counts and lights green; a disconnected one carries none.
        /// </summary>
        [Fact]
        public void A_connected_member_is_in_the_world()
        {
            var member = new AllianceStore.Member { CharacterId = 8411, Rank = 1, JoinedUtcMs = 1791760506416 };
            Assert.Equal(1, Whereabouts(new AllianceProtocol.MemberRow(member, "Online", 200, 1, 9, 1, 17, true)));
            Assert.Null(Whereabouts(new AllianceProtocol.MemberRow(member, "Offline", 200, 1, 9, 1, 17, false)));
        }

        /// <summary>The details' f3 of a member's line, or null.</summary>
        private static long? Whereabouts(AllianceProtocol.MemberRow row)
        {
            var line = ProtoMessage.Parse(AllianceProtocol.MemberLine(row).Build());
            var person = ProtoMessage.Parse(line.Fields.Single(f => f.FieldNumber == 3).BytesValue);
            var entry = ProtoMessage.Parse(person.Fields.Single(f => f.FieldNumber == 1).BytesValue);
            var details = ProtoMessage.Parse(entry.Fields.Single(f => f.FieldNumber == 5).BytesValue);
            return details.Fields.FirstOrDefault(f => f.FieldNumber == 3)?.VarIntValue;
        }

        /// <summary>
        /// The card (mcl) shows each member with his rank, the f6 of the person's oneof, its f4
        /// always there; the details (f5) of the members tab made the client throw.
        /// </summary>
        [Fact]
        public void The_card_shows_each_member_with_his_rank()
        {
            var leader = new AllianceProtocol.MemberRow(
                new AllianceStore.Member { CharacterId = 8412, Rank = 1, JoinedUtcMs = 1 }, "Leader", 200, 1, 9, 1, 17, true);
            var rank = new AllianceStore.Rank { Id = 1, Name = "Jefe", Icon = 3, Order = 0 };

            foreach (var shownRank in new[] { rank, null })
            {
                var mcl = ProtoMessage.Parse(AllianceProtocol.BuildCardView(Absinto, Pb.New(), new[] { (leader, shownRank) }));
                var person = ProtoMessage.Parse(mcl.Fields.Single(f => f.FieldNumber == 1).BytesValue);
                Assert.Equal(8412, person.Fields.Single(f => f.FieldNumber == 2).VarIntValue);
                var entry = ProtoMessage.Parse(person.Fields.Single(f => f.FieldNumber == 1).BytesValue);
                Assert.Equal("Leader", System.Text.Encoding.UTF8.GetString(entry.Fields.Single(f => f.FieldNumber == 2).BytesValue));
                Assert.DoesNotContain(entry.Fields, f => f.FieldNumber == 5);
                var ldt = ProtoMessage.Parse(entry.Fields.Single(f => f.FieldNumber == 6).BytesValue);
                Assert.Single(ldt.Fields, f => f.FieldNumber == 4);
                if (shownRank != null) Assert.Equal("Jefe", System.Text.Encoding.UTF8.GetString(ldt.Fields.Single(f => f.FieldNumber == 2).BytesValue));
            }
        }

        /// <summary>
        /// The card's numbers where the client reads them (Ghidra): in the lel, f3 the territories
        /// (the directory's «Territorios»), f4 the tax collectors and f5 the members; in the mcl, f2
        /// the subareas of its prisms (the card's «Prismas») and f4 its leader.
        /// </summary>
        [Fact]
        public void The_card_counts_where_the_client_reads_them()
        {
            var card = ProtoMessage.Parse(AllianceProtocol.Card(Absinto, new AllianceStore.Profile(), "L", 3,
                                                                territories: 2, taxCollectors: 1).Build());
            Assert.Equal(2, card.Fields.Single(f => f.FieldNumber == 3).VarIntValue);
            Assert.Equal(1, card.Fields.Single(f => f.FieldNumber == 4).VarIntValue);
            Assert.Equal(3, card.Fields.Single(f => f.FieldNumber == 5).VarIntValue);

            var mcl = ProtoMessage.Parse(AllianceProtocol.BuildCardView(
                Absinto, Pb.New(), Array.Empty<(AllianceProtocol.MemberRow, AllianceStore.Rank)>(), new[] { 178, 179 }, 8412));
            Assert.Equal(new byte[] { 0xB2, 0x01, 0xB3, 0x01 }, mcl.Fields.Single(f => f.FieldNumber == 2).BytesValue);
            Assert.Equal(8412, mcl.Fields.Single(f => f.FieldNumber == 4).VarIntValue);
        }

        /// <summary>
        /// The «Alliance fight» switch: kept per character, answered with the hon {f5 status}, 4
        /// when on and nothing when off -- its f1 is a prequalification's end, none here --, and
        /// carried by the map's actor (f1 of its alliance option).
        /// </summary>
        [Fact]
        public void The_alliance_fight_switch_is_kept_and_answered()
        {
            AllianceStore.Create(8413, "Jondo Guerreros", "JGU", 408, 27, 3116544, 0, 15859463);
            var member = new DatabaseManager.DbCharacter { Id = 8413, Name = "Guerrero", Level = 200, Breed = 2 };

            AllianceStore.SetTakesPartInAva(8413, true);
            Assert.Equal(AllianceProtocol.AvaNotAggressable, AllianceProtocol.AggressableStatus(8413));
            Assert.Equal(Hex("2804"), AllianceProtocol.BuildAggressableStatus(AllianceProtocol.AggressableStatus(8413)));
            Assert.Equal(AllianceProtocol.AvaNotAggressable, AllianceStatusOption(ConnectionProtocol.BuildPlayerActorBlock(member, 341, 5, 65924386)));

            AllianceStore.SetTakesPartInAva(8413, false);
            Assert.Equal(AllianceProtocol.NotAggressable, AllianceProtocol.AggressableStatus(8413));
            Assert.Empty(AllianceProtocol.BuildAggressableStatus(AllianceProtocol.AggressableStatus(8413)));
            Assert.Equal(0, AllianceStatusOption(ConnectionProtocol.BuildPlayerActorBlock(member, 341, 5, 65924386)));
        }

        /// <summary>The f1 of the actor's alliance option, f5 { f5 { f1 ... } }, or 0.</summary>
        private static long AllianceStatusOption(byte[] actor)
        {
            var root = ProtoMessage.Parse(actor);
            var details = ProtoMessage.Parse(root.Fields.Single(f => f.FieldNumber == 2).BytesValue);
            var kind = ProtoMessage.Parse(details.Fields.Single(f => f.FieldNumber == 1).BytesValue);
            var humanoid = ProtoMessage.Parse(kind.Fields.Single(f => f.FieldNumber == 5).BytesValue);
            var body = ProtoMessage.Parse(humanoid.Fields.Single(f => f.FieldNumber == 3).BytesValue);

            foreach (var option in body.Fields.Where(f => f.FieldNumber == 5 && f.WireType == 2))
            {
                var alliance = ProtoMessage.Parse(option.BytesValue).Fields.FirstOrDefault(f => f.FieldNumber == 5 && f.WireType == 2);
                if (alliance == null) continue;
                var status = ProtoMessage.Parse(alliance.BytesValue).Fields.FirstOrDefault(f => f.FieldNumber == 1);
                return status == null ? 0 : status.VarIntValue;
            }
            return -1;
        }

        /// <summary>The f5 { f5 { f2 ... } } of the actor's humanoid body, or null.</summary>
        private static byte[]? AllianceOption(byte[] actor)
        {
            var root = ProtoMessage.Parse(actor);
            var details = ProtoMessage.Parse(root.Fields.Single(f => f.FieldNumber == 2).BytesValue);
            var kind = ProtoMessage.Parse(details.Fields.Single(f => f.FieldNumber == 1).BytesValue);
            var humanoid = ProtoMessage.Parse(kind.Fields.Single(f => f.FieldNumber == 5).BytesValue);
            var body = ProtoMessage.Parse(humanoid.Fields.Single(f => f.FieldNumber == 3).BytesValue);

            foreach (var option in body.Fields.Where(f => f.FieldNumber == 5 && f.WireType == 2))
            {
                var inner = ProtoMessage.Parse(option.BytesValue);
                var alliance = inner.Fields.FirstOrDefault(f => f.FieldNumber == 5 && f.WireType == 2);
                if (alliance == null) continue;
                var block = ProtoMessage.Parse(alliance.BytesValue).Fields.FirstOrDefault(f => f.FieldNumber == 2);
                if (block != null) return block.BytesValue;
            }
            return null;
        }
    }
}
