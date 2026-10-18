using System.IO;
using System.Threading.Tasks;
using Jondo.Unity.Launcher;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.Tests.Economy;
using Microsoft.Data.Sqlite;
using Xunit;
using QuestEngine = Jondo.Unity.Server.Managers.Quests;

namespace Jondo.Unity.Tests.Quests
{
    /// <summary>
    /// A wanted poster's «start the quest» button (ido): the quest starts as from an NPC, and
    /// pressed again it answers what the real server did on the three posters of Astrub.
    /// </summary>
    [Collection(AlmanaxTests.AlmanaxClock)]
    public sealed class QuestFromDocumentTests
    {
        private const long Character = 8_890_100_001, Account = 8_890_000_001;
        private const int Poster = 1565;

        private static void Forget()
        {
            using var connection = new SqliteConnection(DatabaseManager.WorldConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM CharacterQuests WHERE CharacterId = $c;";
            command.Parameters.AddWithValue("$c", Character);
            try { command.ExecuteNonQuery(); } catch (SqliteException) { }
        }

        private static byte[] Ido(int quest) => ConnectionProtocol.Push("ido", Pb.New().Var(1, quest).Build());

        [Fact]
        public async Task A_poster_starts_its_quest_once()
        {
            if (!File.Exists(Paths.WorldDb) || !File.Exists(Paths.QuestsJson)) return;
            QuestEngine.Load();
            Forget();
            try
            {
                await using var pipe = await ClientPipe.OpenAsync(Account, Character, 191105026, "Cazador");
                using (SessionContext.Push(pipe.Session))
                {
                    pipe.Session.State.CharacterLevel = 150;
                    QuestEngine.LoadFrom(Character);

                    // No document open: the button does not exist, nothing starts.
                    await QuestHandler.StartFromDocumentAsync(pipe.ToClient, Ido(Poster));
                    Assert.False(QuestEngine.Log!.Active(Poster));

                    pipe.Session.State.DocumentOpen = true;
                    await QuestHandler.StartFromDocumentAsync(pipe.ToClient, Ido(Poster));
                    Assert.Equal(QuestProtocol.BuildQuestStarted(Poster), await pipe.Next(Op.Ief));
                    Assert.True(QuestEngine.Log!.Active(Poster));
                    await pipe.Next(Op.Idu);

                    // Again: «No puedes empezar una misión ya empezada», as the capture answers.
                    await QuestHandler.StartFromDocumentAsync(pipe.ToClient, Ido(Poster));
                    while (true)
                    {
                        var (opcode, body) = await pipe.NextAny();
                        if (opcode != Op.Lqn) continue;
                        Assert.Equal(ConnectionProtocol.BuildInfoMessage(InfoMessages.Warning, QuestHandler.AlreadyStarted), body);
                        break;
                    }
                }
            }
            finally
            {
                Forget();
            }
        }
    }
}
