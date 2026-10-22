using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jondo.Unity.Launcher;
using Jondo.Unity.Server.Network;
using Xunit;

namespace Jondo.Unity.Tests.Network
{
    /// <summary>
    /// The world-entry sequence: what the manifest must carry and what it must never carry.
    /// </summary>
    /// <remarks>
    /// Entering the world stopped being a replay of three .bin files and became
    /// <c>content/world/entry.json</c>, one row per frame with the protobuf fields written out. The
    /// point of that change is reviewability — the quest journal and the achievements of the
    /// recorded account travelled for months because nobody can diff a blob. The .bin files
    /// themselves are no longer in the repository: nothing reads them any more.
    /// </remarks>
    // WorldEntryContent.Load empties the blocks and fills them again: whoever reads them meanwhile
    // finds them half there. Every test class that loads the manifest goes in this collection.
    [Collection("world entry content")]
    public class WorldEntryContentTests
    {
        private const string Manifest = "world/entry.json";

        private static readonly string[] Blocks =
        {
            WorldEntry.BlockAfterCharacter, WorldEntry.BlockAfterConfirm, WorldEntry.BlockMap,
        };

        /// <summary>
        /// What the manifest deliberately leaves out: the recorded account's own state.
        /// </summary>
        /// <remarks>
        /// These are not "not decoded yet". Each one was read and found to belong to the player who
        /// was recorded, so it is dropped rather than rebuilt, and the comparison below has to
        /// expect them missing or it would fail for the one reason that is correct.
        /// </remarks>
        private static readonly HashSet<string> Dropped = new HashSet<string>
        {
            "idu",   // 265 frames, one per quest of that account
            "idr",   // the same journal again, whole: 261 under way and 622 called finished
            "iel",   // its followed quests, 1869 and 2406, in the little box in the corner
            "mft",   // its 954 achievements
            "ivi",   // its counters: 9,694 pairs of id and value
        };

        /// <summary>
        /// Quest ids belonging to the recorded player. None of them may reach the wire.
        /// </summary>
        /// <remarks>
        /// 1869 is "El daño de Búril" and 2406 "Cuando el despertar no es más que un sueño". They
        /// outlived the removal of the journal because they travelled in a different message: the
        /// iel is the followed-quest box, not the journal, and it REPLACES the list rather than
        /// adding to it. So the server sent the right journal -- the log said so -- and the box
        /// still showed two quests belonging to somebody else and none belonging to the character.
        /// A quest just picked up did appear, because the live ief adds it; logging back in made it
        /// vanish again.
        /// </remarks>
        private static readonly int[] RecordedQuests = { 1869, 2406 };

        /// <summary>
        /// Frames the server fills in itself, so the manifest carries only the envelope.
        /// </summary>
        /// <remarks>
        /// The body is deliberately empty here and replaced in <c>WorldEntry.Rebuilt</c> with one
        /// built from the database — the character, its jobs, its spells, its inventory, its
        /// shortcut bars. A body left in one of them would be the recorded character's, which is the
        /// very thing the rebuild exists to stop.
        /// </remarks>
        private static readonly HashSet<string> Rebuilt = new HashSet<string>
        {
            "kva", "hms", "ivx", "itg",
        };

        private static bool Available(out string reason)
        {
            reason = "";
            if (!File.Exists(Paths.ContentFile(Manifest)))
            {
                reason = "no manifest";
                return false;
            }

            return true;
        }

        [Fact]
        public void Nothing_that_belongs_to_the_recorded_account_survives_in_the_manifest()
        {
            if (!Available(out _)) return;

            WorldEntryContent.Load(Paths.ContentFile(Manifest));

            // The check that would have caught idr months earlier: not "is idu gone", which was
            // true and useless, but "is anything at all left carrying that journal". A frame is
            // named by its opcode, so the whole list is asked rather than the one that was noticed.
            var found = new List<string>();
            foreach (string block in Blocks)
            {
                foreach (var row in WorldEntryContent.Rows(block))
                {
                    if (Dropped.Contains(row.Opcode)) found.Add($"{block}: {row.Opcode}");
                }
            }

            Assert.True(found.Count == 0,
                "the manifest still sends: " + string.Join(", ", found.Distinct()));
        }

        [Fact]
        public void The_three_blocks_are_all_there_and_none_is_empty()
        {
            if (!Available(out _)) return;

            WorldEntryContent.Load(Paths.ContentFile(Manifest));

            // A block that reads as empty is the failure mode with no symptom until a client tries
            // to connect: the server sends nothing, the client waits, and the log says nothing was
            // wrong. Worth one assertion.
            foreach (string block in Blocks)
            {
                Assert.True(WorldEntryContent.Count(block) > 0, $"block {block} is empty");
            }
        }

        [Fact]
        public void The_two_shortcut_bars_are_told_apart_by_the_manifest()
        {
            if (!Available(out _)) return;

            WorldEntryContent.Load(Paths.ContentFile(Manifest));

            // There are two itg frames and the only thing separating them now is this label. The
            // server used to look inside the payload — f6 items, f9 spells — and that payload is no
            // longer carried, so if the label ever stops matching what WorldEntry compares against,
            // BOTH bars come out as the item bar and the spell bar is empty. That has happened once
            // already by another route, and it is invisible until a client connects.
            var bars = new List<string>();
            foreach (string block in Blocks)
            {
                foreach (var row in WorldEntryContent.Rows(block))
                {
                    if (row.Opcode == "itg") bars.Add(row.Built);
                }
            }

            Assert.Equal(2, bars.Count);
            Assert.Single(bars, bar => bar == WorldEntry.SpellBarLabel);

            // And the RIGHT one of the two, which the count alone does not say. The label was
            // written backwards once, from a comment in the source that claimed "f6 for items and
            // f9 for spells" while the code three methods below it did the opposite and was right.
            // Measured on the capture instead of read from anybody's prose: the first itg of the map
            // block is the one whose slots carry field 6, and its values are real spell ids.
            var rows = WorldEntryContent.Rows(WorldEntry.BlockMap)
                .Where(r => r.Opcode == "itg").ToList();

            Assert.Equal(2, rows.Count);
            Assert.Equal(WorldEntry.SpellBarLabel, rows[0].Built);
            Assert.NotEqual(WorldEntry.SpellBarLabel, rows[1].Built);
        }

        [Fact]
        public void The_frames_the_server_rebuilds_carry_no_body_in_the_manifest()
        {
            if (!Available(out _)) return;

            // The guard that matters, and the one that would have caught every leak this file has
            // had. Not "is opcode X gone" — that question has been answered yes while the same data
            // walked out through opcode Y twice — but "is there any body left in the frames that
            // describe the character". Whatever such a body held would be the recorded one's.
            WorldEntryContent.Load(Paths.ContentFile(Manifest));

            var carrying = new List<string>();
            foreach (string block in Blocks)
            {
                foreach (var row in WorldEntryContent.Rows(block))
                {
                    if (!Rebuilt.Contains(row.Opcode)) continue;
                    byte[]? payload = ConnectionProtocol.ReadPayload(row.Frame, row.Opcode);
                    if (payload is { Length: > 0 }) carrying.Add($"{block}: {row.Opcode}");
                }
            }

            Assert.True(carrying.Count == 0,
                "the manifest still carries a body in: " + string.Join(", ", carrying.Distinct()));
        }

        [Fact]
        public void The_jobs_frame_keeps_the_body_the_server_reads_from_it()
        {
            if (!Available(out _)) return;

            // The irq is the only one the server rebuilds AND needs to read: its body says WHICH professions
            // exist and in what order the client wants them, which are game data. Taking it away left
            // the character entering without a single profession for hours, and silently: ReadPayload
            // returns an empty array instead of null, so the guard did not fire, the loop did not go
            // round once and the console line suppressed itself for being zero.
            //
            // The catalogue is no substitute: the Jobs table brings 23 and the capture lists 20.
            WorldEntryContent.Load(Paths.ContentFile(Manifest));

            var irq = WorldEntryContent.Rows(WorldEntry.BlockAfterCharacter)
                .FirstOrDefault(row => row.Opcode == "irq");

            Assert.NotNull(irq);

            byte[]? payload = ConnectionProtocol.ReadPayload(irq!.Frame, "irq");
            Assert.NotNull(payload);
            Assert.True(payload!.Length > 0, "the manifest's irq comes without a body");

            int oficios = ProtoMessage.Parse(payload).Fields.Count(f => f.FieldNumber == 1);
            Assert.Equal(20, oficios);
        }

        [Fact]
        public void The_followed_quest_box_of_the_recorded_player_is_not_sent()
        {
            // The frame itself. It was the last piece of that player's quest state still on the
            // wire after the journal had been taken out of both blocks.
            WorldEntryContent.Load(Paths.ContentFile(Manifest));

            foreach (string block in new[]
                     { WorldEntry.BlockAfterCharacter, WorldEntry.BlockAfterConfirm, WorldEntry.BlockMap })
            {
                Assert.DoesNotContain(WorldEntryContent.Rows(block), row => row.Opcode == "iel");
            }
        }

        [Fact]
        public void And_neither_are_the_quest_ids_it_carried()
        {
            // Belt and braces, and not the same assertion: this one would still fail if those two
            // quests came back inside some other opcode, which is exactly how they survived the
            // first two attempts at removing them.
            WorldEntryContent.Load(Paths.ContentFile(Manifest));

            foreach (int questId in RecordedQuests)
            {
                byte[] varint = Varint(questId);

                foreach (string block in new[]
                         { WorldEntry.BlockAfterCharacter, WorldEntry.BlockAfterConfirm, WorldEntry.BlockMap })
                {
                    foreach (var row in WorldEntryContent.Rows(block))
                    {
                        Assert.False(Contains(row.Frame, varint),
                                     $"quest {questId} of the recorded player still travels in a {row.Opcode}");
                    }
                }
            }
        }

        private static byte[] Varint(int value)
        {
            var bytes = new List<byte>();
            uint left = (uint)value;
            while (true)
            {
                byte piece = (byte)(left & 0x7F);
                left >>= 7;
                if (left == 0) { bytes.Add(piece); break; }
                bytes.Add((byte)(piece | 0x80));
            }
            return bytes.ToArray();
        }

        private static bool Contains(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= haystack.Length; i++)
            {
                bool hit = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j]) { hit = false; break; }
                }

                if (hit) return true;
            }
            return false;
        }
    }
}
