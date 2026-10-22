using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Handlers
{
    /// <summary>
    /// The guild chest: opening it, changing tab, and whether a move is allowed. The moves
    /// themselves are any storage's: see <see cref="StorageHandler"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured on "Interactivos varios/entrar en banco bonta-abrir cofre gremio-usarlo-abrir cofre
    /// personal del banco.pcapng", frame numbers being positions in <c>hilo.tramas</c>:
    ///
    ///   26  C iwo { f1: 28538, f2: 524415 }          the chest, skill instance and element
    ///   27  S iwn { f1: 1, f2: 524415, f4: 184, f5: character }
    ///   28  S ivl { the tabs }
    ///   29  S kbk { f1: 22, f2: 1, f3: 100 }         the window, on tab 1
    ///   30  S iwb {}                                  tab 1, empty
    ///   31  S jlo { f1: "Hero-Example" }             who has it open
    ///   32  S jlq { f1: "Hero-Example" }
    ///   38  C jll { f2: 2 }                           tab 2, please
    ///   39  S lqn { f1: 1, f2: 654 }                  "No tienes el derecho de consultar el cofre de gremio."
    ///   48  C kcr { f1: 1, f2: 534451715 }  → itd, ium, iun
    ///   61  C kcr { f1: -1, f2: 537272712 } → iua, itc, iun
    ///   68  C kla → 69 S khd { f3: 11 }
    ///
    /// The hpp and hpq of frames 33-34 are left out: they come after using a home potion too
    /// ("Movimiento/utilizar pocima de hogar"), so they belong to something else.
    /// </para>
    /// <para>
    /// Inferred, and said where it is done: the refusals other than 654 (put in, take out, no
    /// guild, that type of item -- the client's own sentences 655, 656, 659, 661), what a granted
    /// tab change answers (the tab's content), and who else is told the list of viewers.
    /// </para>
    /// </remarks>
    public static class GuildChestHandler
    {
        /// <summary>The item a move is about, read before it moves: from the bag going in, from the tab coming out.</summary>
        internal static (int Gid, IReadOnlyList<Equipment.ItemEffect> Effects)? ItemOfMove(StorageHandler.Window window, int quantity, long uid)
        {
            if (quantity > 0) return Equipment.ByUid(uid) is { } bagged ? (bagged.Template, bagged.Effects) : null;
            foreach (var stored in StorageStacks.ItemsOf(window.Place))
                if (stored.Uid == uid) return (stored.Gid, Equipment.ParseEffects(stored.Effects));
            return null;
        }

        /// <summary>
        /// A move into or out of the chest, in the guild's journal (its chest list): «{0} ha
        /// depositado/retirado {1} x {2}…», the tab it reached or left.
        /// </summary>
        internal static void LogMove(StorageHandler.Window window, int quantity, long uid,
                                     (int Gid, IReadOnlyList<Equipment.ItemEffect> Effects) item)
        {
            var state = SessionContext.State;
            int count = Math.Abs(quantity);
            bool deposit = quantity > 0;
            GuildStore.Log(window.GuildId, GuildLogProtocol.Chest,
                GuildLogProtocol.ChestLine(state.CharacterId, state.CharacterName,
                    deposit ? GuildLogProtocol.ChestMove.Deposit : GuildLogProtocol.ChestMove.Withdraw, count,
                    ConnectionProtocol.ItemBody(item.Gid, item.Effects, count, uid),
                    deposit ? 0 : window.Tab, deposit ? window.Tab : 0),
                state.CharacterId, state.CharacterName);
        }

        /// <summary>The chest was used: open it on the first tab, if the character may look at it.</summary>
        public static async Task OpenAsync(NetworkStream? stream, int elementId, int skillId)
        {
            long character = SessionContext.State.CharacterId;
            await StorageHandler.SendAsync(stream, Op.Iwn,
                ConnectionProtocol.BuildElementInUse(elementId, skillId, character));

            var guild = GuildStore.GuildOf(character);
            if (guild == null)
            {
                await RefuseAsync(stream, GuildChests.NoGuild);
                return;
            }

            var tab = GuildChests.TabOf(guild.Id, 1)!;
            if (!GuildChests.RightsOf(character).Contains(tab.ConsultRight))
            {
                await RefuseAsync(stream, GuildChests.NoRightToLook);
                return;
            }

            var window = new StorageHandler.Window
            {
                Kind = StorageHandler.Kind.GuildChest,
                Place = StorageStacks.GuildChest(guild.Id, tab.Index),
                MapId = SessionContext.State.MapId,
                ElementId = elementId,
                GuildId = guild.Id,
                Tab = tab.Index,
            };

            await StorageHandler.SendAsync(stream, Op.Ivl, StorageProtocol.BuildGuildChestTabs(GuildChests.TabsOfGuild(guild.Id)));
            await StorageHandler.SendAsync(stream, Op.Kbk, StorageProtocol.BuildGuildChestOpened(tab.Index));
            await StorageHandler.OpenAsync(stream, window, null);

            await StorageHandler.SendAsync(stream, Op.Jlo, StorageProtocol.BuildGuildChestViewers(ViewersOf(window)));
            await StorageHandler.SendAsync(stream, Op.Jlq, StorageProtocol.BuildGuildChestViewer(NameOf(SessionContext.Current)));
            await ViewersChangedAsync(window, SessionContext.Current.Id);

            Console.WriteLine($"[Guild chest] {character} opened the chest of guild {guild.Id}, tab {tab.Index}.");
        }

        /// <summary>
        /// jll { f2: tab }: another tab. Refused with 654 without the right to look at it,
        /// frames 38-39; granted, its content (inference). False when no guild chest is open.
        /// </summary>
        public static async Task<bool> SelectTabAsync(NetworkStream? stream, byte[] payload)
        {
            var window = StorageHandler.Current;
            if (window == null || window.Kind != StorageHandler.Kind.GuildChest) return false;

            byte[]? jll = ConnectionProtocol.ReadPayload(payload, Op.Jll);
            if (jll == null) return true;

            int index = 0;
            foreach (var field in ProtoMessage.Parse(jll).Fields)
            {
                if (field.FieldNumber == 2 && field.WireType == 0) index = (int)field.VarIntValue;
            }

            var rights = GuildChests.RightsOf(SessionContext.State.CharacterId);
            var tab = GuildChests.Tabs.Count >= index && index >= 1 ? GuildChests.Tabs[index - 1] : null;
            if (tab == null || GuildChests.TabOf(window.GuildId, index) == null || !rights.Contains(tab.ConsultRight))
            {
                await RefuseAsync(stream, GuildChests.NoRightToLook);
                return true;
            }

            window.Tab = tab.Index;
            window.Place = StorageStacks.GuildChest(window.GuildId, tab.Index);
            await StorageHandler.SendAsync(stream, Op.Iwb, ConnectionProtocol.BuildStorageContent(StorageStacks.ItemsOf(window.Place)));
            return true;
        }

        /// <summary>
        /// Whether this move may happen: the right to put in or take out of the tab open, the item
        /// type the tab takes, and still being in that guild. Refused with the client's sentence.
        /// </summary>
        internal static async Task<bool> MayMoveAsync(NetworkStream? stream, StorageHandler.Window window, bool deposit, long uid)
        {
            long character = SessionContext.State.CharacterId;
            var member = GuildStore.MemberOf(character);
            var tab = GuildChests.TabOf(window.GuildId, window.Tab);
            if (member == null || member.GuildId != window.GuildId || tab == null)
            {
                await RefuseAsync(stream, GuildChests.NoGuild);
                return false;
            }

            var rights = GuildChests.RightsOf(character);
            if (!rights.Contains(deposit ? tab.DepositRight : tab.WithdrawRight))
            {
                await RefuseAsync(stream, deposit ? GuildChests.NoRightToPut : GuildChests.NoRightToTake);
                return false;
            }

            if (deposit)
            {
                var item = HavenBagStore.FromInventory(character, uid);
                int type = item == null ? 0 : ItemTypeOf(item.Gid);
                if (type != 0 && !tab.ItemTypes.Contains(type))
                {
                    await RefuseAsync(stream, GuildChests.TypeRefused);
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// An item sent to another tab from the open one, the item's «Transferir» menu:
        /// <c>kgy { 1 uid, 2 the tab it goes to, 3 how many }</c>, read off the client
        /// (GuildChestStorageUI.OnValidQuantity builds its action with the item, the quantity and
        /// the tab, which emq writes in f1, f3 and f2). It needs the right to take out of this tab
        /// and to put into that one, which must take its type. The open tab loses it as a
        /// withdrawal does (itc, or itd with what stays), whoever looks at the other tab gets it as
        /// a deposit (itd), and the journal writes the transfer, from tab and to tab (the chest
        /// line's move 2). INFERRED: the answer, as no capture moves an item between tabs.
        /// </summary>
        public static async Task<bool> TransferAsync(NetworkStream? stream, byte[] payload)
        {
            var window = StorageHandler.Current;
            byte[]? kgy = ConnectionProtocol.ReadPayload(payload, "kgy");
            if (window == null || window.Kind != StorageHandler.Kind.GuildChest || kgy == null) return false;

            long uid = 0;
            int destination = 0, quantity = 0;
            foreach (var field in ProtoMessage.Parse(kgy).Fields)
            {
                if (field.WireType != 0) continue;
                if (field.FieldNumber == 1) uid = field.VarIntValue;
                else if (field.FieldNumber == 2) destination = (int)field.VarIntValue;
                else if (field.FieldNumber == 3) quantity = (int)field.VarIntValue;
            }

            if (!await MayMoveAsync(stream, window, deposit: false, uid)) return true;
            var to = GuildChests.TabOf(window.GuildId, destination);
            if (to == null || destination == window.Tab) return true;
            if (!GuildChests.RightsOf(SessionContext.State.CharacterId).Contains(to.DepositRight))
            {
                await RefuseAsync(stream, GuildChests.NoRightToPut);
                return true;
            }
            var stored = StorageStacks.Find(window.Place, uid);
            if (stored == null) return true;
            int type = ItemTypeOf(stored.Gid);
            if (type != 0 && !to.ItemTypes.Contains(type))
            {
                await RefuseAsync(stream, GuildChests.TypeRefused);
                return true;
            }

            var moved = StorageStacks.Move(window.Place, StorageStacks.GuildChest(window.GuildId, destination), uid, quantity);
            if (moved == null) return true;

            (string opcode, byte[] body) left = moved.Left == null
                ? (Op.Itc, ConnectionProtocol.BuildItemGone(moved.FromUid))
                : (Op.Itd, ConnectionProtocol.BuildItemArrived(1, moved.Left));
            await StorageHandler.SendAsync(stream, left.opcode, left.body);
            await TellLookersAsync(window.GuildId, window.Tab, ConnectionProtocol.Push(left.opcode, left.body));
            await TellLookersAsync(window.GuildId, destination,
                ConnectionProtocol.Push(Op.Itd, ConnectionProtocol.BuildItemArrived(1, moved.Arrived)));

            var state = SessionContext.State;
            GuildStore.Log(window.GuildId, GuildLogProtocol.Chest,
                GuildLogProtocol.ChestLine(state.CharacterId, state.CharacterName, GuildLogProtocol.ChestMove.Transfer,
                    moved.Moved, ConnectionProtocol.ItemBody(stored.Gid, Equipment.ParseEffects(stored.Effects), moved.Moved, uid),
                    window.Tab, destination),
                state.CharacterId, state.CharacterName);
            Console.WriteLine($"[Guild chest] {state.CharacterName} moves {moved.Moved} x {stored.Gid} from tab {window.Tab} to {destination}.");
            return true;
        }

        /// <summary>Everybody else with that tab of the guild's chest open, on the chest's map.</summary>
        private static async Task TellLookersAsync(long guildId, int tab, byte[] frame)
        {
            foreach (var session in SessionRegistry.InWorld())
            {
                if (session.Id == SessionContext.Current.Id) continue;
                var theirs = session.State.Storage;
                if (theirs == null || theirs.Kind != StorageHandler.Kind.GuildChest || theirs.GuildId != guildId
                    || theirs.Tab != tab || theirs.MapId != session.State.MapId) continue;
                try { await session.SendAsync(frame).ConfigureAwait(false); }
                catch (Exception ex) { Console.WriteLine($"[Guild chest] {session.CharacterId} could not be told of a transfer: {ex.Message}"); }
            }
        }

        /// <summary>
        /// Everybody else looking at the same guild chest gets the list of who is (jlo). Nobody
        /// else was there in the capture, so to whom and when is an inference.
        /// </summary>
        internal static async Task ViewersChangedAsync(StorageHandler.Window window, Guid except)
        {
            var names = ViewersOf(window);
            byte[]? frame = null;
            foreach (var session in SessionRegistry.InWorld())
            {
                if (session.Id == except) continue;
                var theirs = session.State.Storage;
                if (theirs == null || theirs.Kind != StorageHandler.Kind.GuildChest || theirs.GuildId != window.GuildId) continue;
                if (theirs.MapId != session.State.MapId) continue;

                frame ??= ConnectionProtocol.Push(Op.Jlo, StorageProtocol.BuildGuildChestViewers(names));
                try
                {
                    await session.SendAsync(frame).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Guild chest] {session.CharacterId} could not be told who is looking: {ex.Message}");
                }
            }
        }

        /// <summary>The names of the characters who have this guild's chest open, this one included.</summary>
        private static List<string> ViewersOf(StorageHandler.Window window)
        {
            var names = new List<string>();
            foreach (var session in SessionRegistry.InWorld())
            {
                var theirs = session.State.Storage;
                if (theirs == null || theirs.Kind != StorageHandler.Kind.GuildChest || theirs.GuildId != window.GuildId) continue;
                if (theirs.MapId != session.State.MapId) continue;
                names.Add(NameOf(session));
            }

            string mine = NameOf(SessionContext.Current);
            if (SessionContext.State.Storage == window && !names.Contains(mine)) names.Insert(0, mine);
            return names;
        }

        private static string NameOf(GameSession session)
        {
            if (session.State.CharacterName is { Length: > 0 } name) return name;
            return DatabaseManager.GetCharacterById(session.CharacterId)?.Name ?? "";
        }

        private static Task RefuseAsync(NetworkStream? stream, int message)
            => StorageHandler.SendAsync(stream, Op.Lqn,
                ConnectionProtocol.BuildInfoMessage(InfoMessages.Warning, message));

        private static int ItemTypeOf(int gid)
        {
            try
            {
                using var connection = new SqliteConnection(DatabaseManager.WorldConnectionString);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT Type FROM ItemTemplates WHERE Id = $id;";
                command.Parameters.AddWithValue("$id", gid);
                return command.ExecuteScalar() is long type ? (int)type : 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
