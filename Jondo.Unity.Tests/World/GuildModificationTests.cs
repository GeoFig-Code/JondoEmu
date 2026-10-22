using System;
using System.IO;
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

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// A guild's name and emblem changed with its potions: the leader's potion opens the founding
    /// editor in its mode (jje), and the editor's answer (jhv, jhz, or jjg for both) is told jhq.
    /// </summary>
    [Collection("guild raids")]
    public sealed class GuildModificationTests : IDisposable
    {
        private readonly string _file;

        public GuildModificationTests()
        {
            _file = Path.Combine(Path.GetTempPath(), $"jondo-guild-mod-{Guid.NewGuid():N}.db");
            GuildStore.ConnectionStringOverride = $"Data Source={_file}";
        }

        public void Dispose()
        {
            GuildStore.ConnectionStringOverride = null;
            TestDatabases.ReleasePool(_file);
            try { File.Delete(_file); } catch (IOException) { }
        }

        [Fact]
        public void A_new_name_and_emblem_stay()
        {
            var guild = GuildStore.Create(7101, "Jondo", 165, 8, 16744448, 9476018);
            Assert.Equal("Jondo Nuevo", GuildStore.Rename(guild.Id, "Jondo Nuevo").Name);
            var redrawn = GuildStore.SetEmblem(guild.Id, 408, 27, 3116544, 15859463);
            Assert.Equal((408, 27, 3116544, 15859463),
                         (redrawn.EmblemSymbol, redrawn.EmblemSymbolColor, redrawn.EmblemBackground, redrawn.EmblemSymbolRgb));
            Assert.Equal("Jondo Nuevo", GuildStore.GuildOf(7101).Name);

            // The result travels as the founding's: nothing for done, the reason in f1 otherwise.
            Assert.Empty(GuildHandler.BuildResult(GuildHandler.ResultOk));
            Assert.Equal(new byte[] { 0x08, 0x03 }, GuildHandler.BuildResult(GuildHandler.ResultNameUsed));
        }

        [Fact]
        public async Task The_leader_s_potion_renames_the_guild()
        {
            if (!File.Exists(Paths.WorldDb)) return;
            const long account = 8_900_000_201;
            long character = DatabaseManager.CreateCharacter(account, 1, "Renombra-Uno", 4, 0, 1,
                new long[] { -1, -1, -1, -1, -1 }, 191105026, 200, 0, 0, Array.Empty<(int, int)>());
            Assert.True(character > 0);
            try
            {
                var guild = GuildStore.Create(character, "Jondo Viejo", 165, 8, 16744448, 9476018);
                GuildStore.Create(9_999_901, "Jondo Ajeno", 165, 8, 16744448, 9476018);
                await using var pipe = await ClientPipe.OpenAsync(account, character, 990_100_141, "Renombra-Uno");
                using (SessionContext.Push(pipe.Session))
                {
                    Assert.True(await Equipment.GiveAsync(pipe.ToClient, GuildHandler.RenamePotionTemplate, 1));
                    var potion = Equipment.All.Single(i => i.Template == GuildHandler.RenamePotionTemplate);

                    // The editor in its name mode: jje { 3 }.
                    Assert.True(await GuildHandler.TryUsePotionAsync(pipe.ToClient, potion));
                    Assert.Equal(new byte[] { 0x18, 0x01 }, await Until(pipe, Op.Jje));

                    // Another guild's name is refused, and the editor stays with its potion.
                    await GuildHandler.RenameAsync(pipe.ToClient, Jhv("Jondo Ajeno"));
                    Assert.Equal(GuildHandler.BuildResult(GuildHandler.ResultNameUsed), await Until(pipe, Op.Jhq));

                    await GuildHandler.RenameAsync(pipe.ToClient, Jhv("Jondo Nuevo"));
                    Assert.Empty(await Until(pipe, Op.Jhq));
                    Assert.Equal("Jondo Nuevo", GuildStore.ById(guild.Id).Name);
                    Assert.DoesNotContain(Equipment.All, i => i.Template == GuildHandler.RenamePotionTemplate);

                    // Without a potion in use the editor's answer changes nothing.
                    await GuildHandler.RenameAsync(pipe.ToClient, Jhv("Jondo Otro"));
                    Assert.Equal("Jondo Nuevo", GuildStore.ById(guild.Id).Name);
                }
            }
            finally
            {
                DatabaseManager.DeleteCharacter(character, account);
            }
        }

        [Fact]
        public void Gremia_sells_the_guild_s_potions()
        {
            if (!File.Exists(Paths.ContentFile(NpcShops.AuthoredFile))) return;
            NpcShops.Forget();
            NpcShops.ApplyAuthored(Paths.ContentFile(NpcShops.AuthoredFile));
            var catalogue = NpcShops.CatalogueOf(7580);
            Assert.Contains(GuildHandler.RenamePotionTemplate, catalogue);
            Assert.Contains(GuildHandler.ReEmblemPotionTemplate, catalogue);
            Assert.Equal(1, NpcShops.PriceOf(GuildHandler.RenamePotionTemplate));
            NpcShops.Forget();
        }

        private static byte[] Jhv(string name)
            => ConnectionProtocol.Push(Op.Jhv, Pb.New().Bytes(1, System.Text.Encoding.UTF8.GetBytes(name)).Build());

        /// <summary>The next frame of that opcode, past whatever goes before it.</summary>
        private static async Task<byte[]> Until(ClientPipe pipe, string opcode)
        {
            for (int i = 0; i < 20; i++)
            {
                var (got, payload) = await pipe.NextAny();
                if (got == opcode) return payload;
            }
            throw new Xunit.Sdk.XunitException($"{opcode} never came");
        }
    }
}
