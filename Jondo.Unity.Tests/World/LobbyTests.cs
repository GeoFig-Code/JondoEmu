using System;
using System.Collections.Generic;
using System.Linq;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The party search («Búsqueda de grupo»): the capture «busqueda de grupo-crear anuncio-
    /// postularse a otro-autoeliminacion de mi anuncio» for the wire, the client's codes for the rules.
    /// </summary>
    [Collection("lobbies")]
    public class LobbyTests
    {
        public LobbyTests() => Lobbies.Clear();

        private static string Hex(byte[] bytes) => string.Concat(bytes.Select(b => b.ToString("x2")));

        private static byte[] Bytes(string hex) => Enumerable.Range(0, hex.Length / 2)
            .Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();

        private const string MyLobby = "b93afbdc-405f-48c8-aa02-caf93091c790";
        private const string OtherLobby = "4a7a479f-20b6-434f-a1fe-84002b3dfcd0";
        private const string MyUid = "6917336a-8a21-44bd-b866-da4099fabd14";

        /// <summary>The capture's own announcement, as the board carried it.</summary>
        private const string CapturedJos =
            "0801105c20022801322462393361666264632d343035662d343863382d616130322d636166393330393163373930" +
            "3a02c0054201024a1e323032362d30382d31325432323a34303a32312e3934383931383536315a50145a6008a282" +
            "80c8e708121e323032362d30382d31325432323a34303a32312e3934383930383035385a180b20e2022a24363931" +
            "37333336612d386132312d343462642d623836362d6461343039396661626431343a0c4865726f2d4578616d706c" +
            "65626008a28280c8e708121e323032362d30382d31325432323a34303a32312e3934383930383035385a180b20e2" +
            "022a2436393137333336612d386132312d343462642d623836362d6461343039396661626431343a0c4865726f2d" +
            "4578616d706c65";

        private static Lobbies.Player Recorder(DateTime since)
            => new(302677754146, MyUid, "Hero-Example", 11, 354, 0, since);

        private static Lobbies.Player Player(long id, int level = 100)
            => new(id, Lobbies.UidOf(id), "P" + id, 1, level, 0, DateTime.UtcNow);

        /// <summary>
        /// The same message but for its dates, which the real server writes to the nanosecond:
        /// every field equal, in the same order, except f2 of a player and f9 of an announcement.
        /// </summary>
        private static void SameButDates(byte[] expected, byte[] actual, bool isPlayer = false)
        {
            var e = ProtoMessage.Parse(expected).Fields;
            var a = ProtoMessage.Parse(actual).Fields;
            Assert.Equal(e.Select(f => f.FieldNumber), a.Select(f => f.FieldNumber));
            for (int i = 0; i < e.Count; i++)
            {
                int n = e[i].FieldNumber;
                if ((isPlayer && n == 2) || (!isPlayer && n == 9)) continue;
                if (!isPlayer && (n == 11 || n == 12)) SameButDates(e[i].BytesValue, a[i].BytesValue, isPlayer: true);
                else if (e[i].WireType == 2) Assert.Equal(Hex(e[i].BytesValue), Hex(a[i].BytesValue));
                else Assert.Equal(e[i].VarIntValue, a[i].VarIntValue);
            }
        }

        /// <summary>The form (jnv) of the capture: tags [2], kind 1, two players, [704], dungeon 92, open, level 20.</summary>
        [Fact]
        public void The_form_reads_as_the_capture_sends_it()
        {
            var spec = LobbyProtocol.ReadSpec(Bytes("0a0102180128023202c005385c40014814"));
            Assert.Equal(1, spec.Type);
            Assert.Equal(92, spec.ActivityId);
            Assert.Equal(2, spec.GroupSize);
            Assert.True(spec.AutoJoin);
            Assert.Equal(new[] { 2 }, spec.Tags);
            Assert.Equal(new[] { 704 }, spec.Successes);
            Assert.Equal(20, spec.MinLevel);
            Assert.Equal("", spec.Description);
        }

        /// <summary>The announcement (jos) and the player (joa) as the board carries them.</summary>
        [Fact]
        public void An_announcement_goes_as_the_capture_carries_it()
        {
            var owner = Recorder(DateTime.UtcNow);
            var lobby = new Lobbies.Lobby
            {
                Uid = MyLobby, Type = 1, ActivityId = 92, GroupSize = 2, AutoJoin = true,
                Tags = new List<int> { 2 }, Successes = { 704 }, MinLevel = 20, Owner = owner,
            };
            lobby.Members.Add(owner);
            SameButDates(Bytes(CapturedJos), LobbyProtocol.LobbyBlock(lobby).Build());
        }

        /// <summary>The answers and notices the capture has, byte for byte where they carry no date.</summary>
        [Fact]
        public void The_answers_go_as_the_capture_has_them()
        {
            Assert.Equal("1224" + Hex(System.Text.Encoding.UTF8.GetBytes(MyLobby)), Hex(LobbyProtocol.BuildPosted(0, MyLobby)));
            Assert.Equal("0a24" + Hex(System.Text.Encoding.UTF8.GetBytes(MyLobby)), Hex(LobbyProtocol.BuildGone(MyLobby)));
            Assert.Equal("", Hex(LobbyProtocol.BuildDeleted(Lobbies.DeleteCode.Ok)));
            Assert.Equal("0801", Hex(LobbyProtocol.BuildApplied(Lobbies.ApplyCode.Joined)));
            Assert.Equal("", Hex(LobbyProtocol.BuildLeft(Lobbies.LeaveCode.Ok)));
            Assert.Equal("", Hex(LobbyProtocol.BuildMine(null)));
            Assert.Equal("0a24" + Hex(System.Text.Encoding.UTF8.GetBytes(OtherLobby)) + "1a24" + Hex(System.Text.Encoding.UTF8.GetBytes(MyUid)),
                         Hex(LobbyProtocol.BuildMemberOut(OtherLobby, MyUid)));
            Assert.Equal(OtherLobby, LobbyProtocol.ReadString(Bytes("0a24" + Hex(System.Text.Encoding.UTF8.GetBytes(OtherLobby))), 1));

            var jpk = ProtoMessage.Parse(LobbyProtocol.BuildMemberIn(Recorder(DateTime.UtcNow), OtherLobby));
            Assert.Equal(new[] { 1, 2 }, jpk.Fields.Select(f => f.FieldNumber));
            Assert.Equal(OtherLobby, System.Text.Encoding.UTF8.GetString(jpk.Fields[1].BytesValue));
            SameButDates(Bytes("08a28280c8e708121e323032362d30382d31325432323a34303a34352e3531363539363534325a180b20e2022a24" +
                               Hex(System.Text.Encoding.UTF8.GetBytes(MyUid)) + "3a0c4865726f2d4578616d706c65"),
                         jpk.Fields[0].BytesValue, isPlayer: true);
        }

        /// <summary>
        /// The capture's way through: posting, the board, joining another's open announcement at
        /// once, leaving it, and taking one's own down.
        /// </summary>
        [Fact]
        public void The_capture_s_way_through_holds()
        {
            var me = Player(1);
            var other = Player(2);
            var (code, mine) = Lobbies.Create(me, new Lobbies.Spec(1, 92, 2, true, new[] { 2 }, new[] { 704 }, 20, ""));
            Assert.Equal(Lobbies.CreateCode.Ok, code);
            Assert.Equal(new long[] { 1 }, mine.Members.Select(m => m.CharacterId));
            Assert.Equal(Lobbies.CreateCode.AlreadyOwned, Lobbies.Create(me, new Lobbies.Spec(1, 92, 2, true, null, null, 0, "")).Code);

            var (_, theirs) = Lobbies.Create(other, new Lobbies.Spec(1, 98, 4, true, null, null, 20, "ultima sala"));
            Assert.Equal(new[] { mine.Uid, theirs.Uid }, Lobbies.Board(_ => true).Select(l => l.Uid));
            Assert.Empty(Lobbies.Board(id => id != 1 && id != 2));

            // Owning one, he cannot apply elsewhere: the client takes his own down first.
            Assert.Equal(Lobbies.ApplyCode.AlreadyOwner, Lobbies.Apply(me, theirs.Uid).Code);
            Assert.Equal(Lobbies.DeleteCode.NotAuthor, Lobbies.Delete(2, mine.Uid).Code);
            Assert.Equal(Lobbies.DeleteCode.Ok, Lobbies.Delete(1, mine.Uid).Code);

            Assert.Equal(Lobbies.ApplyCode.Joined, Lobbies.Apply(me, theirs.Uid).Code);
            Assert.Equal(theirs, Lobbies.JoinedBy(1));
            Assert.Equal(Lobbies.ApplyCode.AlreadyMember, Lobbies.Apply(me, theirs.Uid).Code);

            var left = Lobbies.Leave(1, theirs.Uid);
            Assert.Equal(Lobbies.LeaveCode.Ok, left.Code);
            Assert.True(left.WasMember);
            Assert.Equal(Lobbies.LeaveCode.NotInLobby, Lobbies.Leave(1, theirs.Uid).Code);
            Assert.Equal(Lobbies.LeaveCode.NotInLobby, Lobbies.Leave(2, theirs.Uid).Code);
        }

        /// <summary>
        /// A closed announcement: applicants wait, the owner answers, the rejected cannot apply
        /// again until thrown away, the minimum level and the group size hold.
        /// </summary>
        [Fact]
        public void The_owner_answers_his_applicants()
        {
            var owner = Player(10);
            var (_, lobby) = Lobbies.Create(owner, new Lobbies.Spec(2, 1234, 3, false, null, null, 50, "misión"));
            var low = Player(11, level: 30);
            var a = Player(12);
            var b = Player(13);
            var c = Player(14);

            Assert.Equal(Lobbies.ApplyCode.CriteriaForPlayer, Lobbies.Apply(low, lobby.Uid).Code);
            Assert.Equal(Lobbies.ApplyCode.Applied, Lobbies.Apply(a, lobby.Uid).Code);
            Assert.Equal(Lobbies.ApplyCode.AlreadyApplying, Lobbies.Apply(a, lobby.Uid).Code);
            Assert.Equal(Lobbies.ApplyCode.Applied, Lobbies.Apply(b, lobby.Uid).Code);
            Assert.Equal(Lobbies.ApplyCode.Applied, Lobbies.Apply(c, lobby.Uid).Code);
            Assert.Equal(new[] { lobby }, Lobbies.AppliedBy(12));

            Assert.Equal(Lobbies.AnswerCode.NotOwned, Lobbies.Answer(12, b.Uid, true).Code);
            Assert.Equal(Lobbies.AnswerCode.Rejected, Lobbies.Answer(10, a.Uid, false).Code);
            Assert.Equal(Lobbies.ApplyCode.BlackListed, Lobbies.Apply(a, lobby.Uid).Code);
            Assert.Equal(new[] { a.Uid }, Lobbies.Free(10, new[] { a.Uid }).Select(p => p.Uid));
            Assert.Equal(Lobbies.ApplyCode.Applied, Lobbies.Apply(a, lobby.Uid).Code);

            Assert.Equal(Lobbies.AnswerCode.Accepted, Lobbies.Answer(10, b.Uid, true).Code);
            Assert.Equal(Lobbies.AnswerCode.Accepted, Lobbies.Answer(10, c.Uid, true).Code);
            Assert.True(lobby.IsFull);
            Assert.Equal(Lobbies.AnswerCode.LobbyFull, Lobbies.Answer(10, a.Uid, true).Code);
            Assert.Equal(Lobbies.AnswerCode.NoApplier, Lobbies.Answer(10, "nadie", true).Code);

            var mine = ProtoMessage.Parse(ProtoMessage.Parse(LobbyProtocol.BuildMine(lobby)).Fields.Single().BytesValue);
            Assert.Equal(new[] { 1, 4 }, mine.Fields.Select(f => f.FieldNumber));

            var gone = Lobbies.Leave(12, lobby.Uid);
            Assert.False(gone.WasMember);
            Assert.Empty(lobby.Applicants);
        }

        /// <summary>The form's limits: the client's kinds and a party's size.</summary>
        [Fact]
        public void The_form_s_limits_hold()
        {
            var p = Player(20);
            Assert.Equal(Lobbies.CreateCode.InvalidActivityType, Lobbies.Create(p, new Lobbies.Spec(0, 1, 2, false, null, null, 0, "")).Code);
            Assert.Equal(Lobbies.CreateCode.InvalidActivityType, Lobbies.Create(p, new Lobbies.Spec(8, 1, 2, false, null, null, 0, "")).Code);
            Assert.Equal(Lobbies.CreateCode.InvalidGroupSize, Lobbies.Create(p, new Lobbies.Spec(1, 1, 1, false, null, null, 0, "")).Code);
            Assert.Equal(Lobbies.CreateCode.InvalidGroupSize, Lobbies.Create(p, new Lobbies.Spec(1, 1, 9, false, null, null, 0, "")).Code);
            Assert.Equal(Lobbies.CreateCode.InvalidDescription,
                         Lobbies.Create(p, new Lobbies.Spec(1, 1, 2, false, null, null, 0, new string('x', 301))).Code);
            Assert.Equal(Lobbies.CreateCode.NotOwned, Lobbies.Edit(20, new[] { 1 }, "x").Code);
            Assert.Equal(Lobbies.CreateCode.Ok, Lobbies.Create(p, new Lobbies.Spec(7, 3, 8, false, null, null, 0, "")).Code);
            var (code, edited) = Lobbies.Edit(20, new[] { 3, 5 }, "nuevo");
            Assert.Equal(Lobbies.CreateCode.Ok, code);
            var jnt = ProtoMessage.Parse(LobbyProtocol.BuildEditNotice(edited));
            Assert.Equal(new[] { 1, 2, 3 }, jnt.Fields.Select(f => f.FieldNumber));
        }
    }
}
