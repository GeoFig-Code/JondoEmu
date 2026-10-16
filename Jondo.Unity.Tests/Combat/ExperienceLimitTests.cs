using System;
using System.Linq;
using System.Threading.Tasks;
using Jondo.Unity.Launcher;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.Tests.Economy;
using Xunit;

namespace Jondo.Unity.Tests.Combat
{
    /// <summary>
    /// The limit on earned experience from the characteristics panel: the two captures «desde panel
    /// caracteristicas limitar experiencia obtenida» and the client's own cut (lg.nzb).
    /// </summary>
    // Loads the world entry manifest, as WorldEntryContentTests does: not at the same time.
    [Collection("world entry content")]
    public class ExperienceLimitTests
    {
        private static byte[] Hex(string hex) => Convert.FromHexString(hex);

        private static void ManifestLoaded() => WorldEntryContent.Load(Paths.ContentFile(WorldEntryContent.AuthoredFile));

        /// <summary>100 to 50 is kts 0832 and hox 086910692032; back is kts 0864 and hox 086910692064.</summary>
        [Theory]
        [InlineData("0832", "086910692032", 50)]
        [InlineData("0864", "086910692064", 100)]
        public async Task The_kts_is_answered_with_the_capture_s_hox(string kts, string hox, int limit)
        {
            ManifestLoaded();
            const long who = 8_999_101;
            await using var pipe = await ClientPipe.OpenAsync(who, who, 191105026, "Limitado");
            try
            {
                using (SessionContext.Push(pipe.Session))
                    await ExperienceLimitHandler.SetAsync(pipe.ToClient, ConnectionProtocol.Push(Op.Kts, Hex(kts)));
                Assert.Equal(Hex(hox), await pipe.Next(Op.Hox));
                Assert.Equal(limit, ExperienceLimit.Of(who));

                // Kept in the base, not only in memory.
                ExperienceLimit.Forget(who);
                Assert.Equal(limit, ExperienceLimit.Of(who));
            }
            finally { ExperienceLimit.Set(who, ExperienceLimit.Full); }
        }

        /// <summary>World entry carries the character's own limit, not the capturer's 100.</summary>
        [Fact]
        public void World_entry_carries_the_character_s_limit()
        {
            ManifestLoaded();
            const long who = 8_999_102;
            var row = WorldEntryContent.Rows(WorldEntry.BlockAfterCharacter).Single(r => r.Opcode == Op.Hox);
            Assert.Equal("ExperienceLimitHandler.BuildSettings", row.Built);
            var character = new DatabaseManager.DbCharacter { Id = who, Name = "Limitado", Level = 200, Breed = 1 };
            try
            {
                Assert.Equal(Hex("086910692064"), ConnectionProtocol.ReadPayload(
                    WorldEntry.Rebuilt(row.Frame, row.Built, character)!, Op.Hox));
                ExperienceLimit.Set(who, 50);
                Assert.Equal(Hex("086910692032"), ConnectionProtocol.ReadPayload(
                    WorldEntry.Rebuilt(row.Frame, row.Built, character)!, Op.Hox));
            }
            finally { ExperienceLimit.Set(who, ExperienceLimit.Full); }
        }

        /// <summary>The client keeps the limit between 0 and 100 and truncates xp × limit / 100.</summary>
        [Theory]
        [InlineData(589, 50, 294)]
        [InlineData(2980, 100, 2980)]
        [InlineData(2980, 150, 2980)]
        [InlineData(2980, 0, 0)]
        [InlineData(2980, -5, 0)]
        [InlineData(1, 99, 0)]
        public void The_cut_is_the_client_s(long experience, int limit, long expected)
            => Assert.Equal(expected, ExperienceLimit.Apply(experience, limit));

        [Fact]
        public void What_is_stored_stays_between_0_and_100()
        {
            const long who = 8_999_103;
            try
            {
                Assert.Equal(ExperienceLimit.Full, ExperienceLimit.Of(who));
                Assert.Equal(100, ExperienceLimit.Set(who, 250));
                Assert.Equal(0, ExperienceLimit.Set(who, -3));
                Assert.Equal(0, ExperienceLimit.Of(who));
            }
            finally { ExperienceLimit.Set(who, ExperienceLimit.Full); }
        }

        /// <summary>A quest's experience goes under the limit; an achievement's, which the client does not warn about, whole.</summary>
        [Fact]
        public async Task A_quest_s_experience_is_limited_and_the_rest_is_not()
        {
            const long who = 8_999_104;
            await using var pipe = await ClientPipe.OpenAsync(who, who, 191105026, "Limitado");
            ExperienceLimit.Set(who, 50);
            try
            {
                using (SessionContext.Push(pipe.Session))
                {
                    pipe.Session.State.CharacterLevel = 200;
                    pipe.Session.State.Experience = ExperienceTable.LevelFloor(200);
                    long before = pipe.Session.State.Experience;

                    await CharacterRewards.GiveExperienceAsync(pipe.ToClient, 1000, serverBonus: false, limited: true);
                    Assert.Equal(before + 500, pipe.Session.State.Experience);
                    await pipe.Next(Op.Kub);
                    Assert.Equal(CharacterRewards.BuildExperienceGained(500), await pipe.Next(Op.Kuf));

                    await CharacterRewards.GiveExperienceAsync(pipe.ToClient, 1000, serverBonus: false);
                    Assert.Equal(before + 1500, pipe.Session.State.Experience);
                }
            }
            finally { ExperienceLimit.Set(who, ExperienceLimit.Full); }
        }
    }
}
