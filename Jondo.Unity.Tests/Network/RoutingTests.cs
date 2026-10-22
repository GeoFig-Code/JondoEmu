using System;
using System.Text;
using Jondo.Unity.Server.Network;
using Xunit;

namespace Jondo.Unity.Tests.Network
{
    /// <summary>
    /// A packet is routed by the type of the message it carries, not by any type its text happens
    /// to mention. Checked once against every client frame of the captures (601 frames, 258 types,
    /// all routed by their own type); these are the ones kept.
    /// </summary>
    public class RoutingTests
    {
        private static string Routed(byte[] frame) => GameNodeProxy.RoutingTextOf(frame, Encoding.UTF8.GetString(frame));

        /// <summary>The client's envelope, { 2: { 1: the Any, 2: the request id } }, as the captures carry it.</summary>
        [Fact]
        public void A_client_frame_routes_by_its_type()
        {
            // The jlt that refuses an application, in «...-valorar candidatura-rechazarla».
            byte[] jlt = Convert.FromHexString("122b0a1e0a13747970652e616e6b616d612e636f6d2f6a6c741207108590d8c69e0510ffffffffffffffffff01");
            Assert.Equal("type.ankama.com/jlt", Routed(jlt));

            // The heartbeat.
            byte[] kqo = Convert.FromHexString("12260a190a13747970652e616e6b616d612e636f6d2f6b716f1202100110ffffffffffffffffff01");
            Assert.Equal("type.ankama.com/kqo", Routed(kqo));
        }

        /// <summary>
        /// A chat line that mentions another type is still a chat line: looked for in the whole frame,
        /// "type.ankama.com/kqq" took the player back to the character list.
        /// </summary>
        [Fact]
        public void A_chat_line_naming_another_type_is_still_chat()
        {
            byte[] line = Pb.New().Str(1, "mirad esto: type.ankama.com/kqq").Build();
            byte[] frame = Pb.New()
                .Msg(2, Pb.New()
                    .Msg(1, Pb.New().Str(1, "type.ankama.com/ktm").Bytes(2, line))
                    .Var(2, -1))
                .Build();

            Assert.Contains("type.ankama.com/kqq", Encoding.UTF8.GetString(frame));
            Assert.Equal("type.ankama.com/ktm", Routed(frame));
        }

        /// <summary>A frame whose envelope cannot be read keeps the whole text, as it routed before.</summary>
        [Fact]
        public void An_unreadable_frame_keeps_its_whole_text()
        {
            byte[] frame = Encoding.UTF8.GetBytes("type.ankama.com/kqo");
            Assert.Equal("type.ankama.com/kqo", Routed(frame));
        }
    }
}
