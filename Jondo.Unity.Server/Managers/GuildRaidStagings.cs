using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using Jondo.Unity.Launcher;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server.Network;
using Jondo.Unity.World.Content;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The guild raids' map stagings: what their maps hold hidden, shown to the raid's members.
    /// </summary>
    /// <remarks>
    /// A raid map carries pieces at alpha 0 in its data -- the Santuario's pedestal objects, its
    /// paper flowers and their green and red, the colour its Obra takes -- and staging sequences
    /// that show them, each by a uniqueId of that map ("pop_lanterne_p1", "#SD fleur_true").
    /// The client plays one when the server sends hpm { f1 progression %, f2 intensity %, f3 the
    /// uniqueId }, in a fight or out of it: its staging service (ebm) listens for it on the
    /// network with no condition, and every map load registers the map's sequences with it
    /// (MapRenderer.HandleMapMetadataLoaded) and gets it ready (LoadMap). Read in Ghidra.
    ///
    /// A map change resets them, so what a raid has shown on a map is kept here ("standing")
    /// and played again, at its end, to whoever of the raid loads that map. What only happens
    /// once -- the red of a wrong flower, the flash of a lost life -- is played, not kept.
    ///
    /// The sequences of each map are the client's (tools/extract_raid_stagings.py).
    /// </remarks>
    public static class GuildRaidStagings
    {
        /// <summary>Progression of a staging played from its start, and of one shown at its end at once.</summary>
        public const int FromStart = 0;
        public const int AtEnd = 100;

        /// <summary>Full intensity.</summary>
        public const int FullIntensity = 100;

        /// <summary>"Perte pv": the red flash every Santuario map plays when the raid loses a life.</summary>
        public const string LifeLost = "Perte pv";

        private static IReadOnlyDictionary<long, IReadOnlyList<(int Id, string Name)>>? _maps;
        private static readonly object _lock = new();

        /// <summary>What each raid has standing on each map, in the order it was shown.</summary>
        private static readonly ConcurrentDictionary<long, ConcurrentDictionary<long, List<int>>> _standing = new();

        private static IReadOnlyDictionary<long, IReadOnlyList<(int Id, string Name)>> Maps
        {
            get
            {
                if (_maps != null) return _maps;
                lock (_lock)
                {
                    _maps ??= Load(Paths.RaidStagingsJson);
                    return _maps;
                }
            }
        }

        private static IReadOnlyDictionary<long, IReadOnlyList<(int Id, string Name)>> Load(string path)
        {
            var maps = new Dictionary<long, IReadOnlyList<(int Id, string Name)>>();
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Raids] {Path.GetFileName(path)} is missing: the raids' maps will show none of their hidden pieces.");
                return maps;
            }
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var map in json.RootElement.GetProperty("maps").EnumerateObject())
                {
                    if (!long.TryParse(map.Name, out long mapId)) continue;
                    maps[mapId] = map.Value.EnumerateArray()
                                     .Select(s => (s[0].GetInt32(), s[1].GetString() ?? ""))
                                     .ToList();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Raids] Could not read {Path.GetFileName(path)}: {ex.Message}");
            }
            return maps;
        }

        // ─── The data ───────────────────────────────────────────────────────────────────────

        /// <summary>A map's staging sequences.</summary>
        public static IReadOnlyList<(int Id, string Name)> Of(long mapId)
            => Maps.TryGetValue(mapId, out var sequences) ? sequences : Array.Empty<(int, string)>();

        /// <summary>The uniqueId a map gives a sequence by its name, or 0 when the map has none so called.</summary>
        public static int IdOf(long mapId, string name)
        {
            foreach (var (id, sequence) in Of(mapId))
                if (sequence == name) return id;
            return 0;
        }

        // ─── What stands ────────────────────────────────────────────────────────────────────

        /// <summary>What a raid has standing on a map, in the order it was shown.</summary>
        public static IReadOnlyList<int> Standing(RaidInstance raid, long mapId)
        {
            if (raid == null || !_standing.TryGetValue(raid.Id, out var maps) || !maps.TryGetValue(mapId, out var ids))
                return Array.Empty<int>();
            lock (ids) return ids.ToList();
        }

        /// <summary>A staging kept standing on a map for a raid: shown again to whoever loads the map.</summary>
        public static void Keep(RaidInstance raid, long mapId, int id)
        {
            if (raid == null || id == 0) return;
            var ids = _standing.GetOrAdd(raid.Id, _ => new()).GetOrAdd(mapId, _ => new List<int>());
            lock (ids)
            {
                ids.Remove(id);
                ids.Add(id);
            }
        }

        /// <summary>Stagings that no longer stand on a map for a raid.</summary>
        public static void Drop(RaidInstance raid, long mapId, params int[] ids)
        {
            if (raid == null || !_standing.TryGetValue(raid.Id, out var maps) || !maps.TryGetValue(mapId, out var kept)) return;
            lock (kept) kept.RemoveAll(ids.Contains);
        }

        /// <summary>A raid over: nothing of it stands any more.</summary>
        public static void Forget(RaidInstance raid)
        {
            if (raid != null) _standing.TryRemove(raid.Id, out _);
        }

        // ─── Showing them ───────────────────────────────────────────────────────────────────

        /// <summary>hpm: { f1 progression %, f2 intensity %, f3 the sequence's uniqueId }.</summary>
        public static byte[] BuildPlay(int id, int progression)
            => Pb.New().VarIfNotZero(1, progression).Var(2, FullIntensity).Var(3, id).Build();

        /// <summary>
        /// A staging played from its start to the raid's members on a map, and kept standing
        /// there when it is to be seen again. Nothing when the map has no sequence so called.
        /// </summary>
        public static async Task PlayAsync(RaidInstance raid, long mapId, string name, bool keep)
        {
            int id = IdOf(mapId, name);
            if (raid == null || id == 0) return;
            if (keep) Keep(raid, mapId, id);
            await TellRaidOnMapAsync(raid, mapId, ConnectionProtocol.Push(Op.Hpm, BuildPlay(id, FromStart)));
        }

        /// <summary>
        /// A map loaded by a raid member: what his raid has standing on it, at its end. The client
        /// keeps the hpm that arrive while it is still loading and plays them once it is ready.
        /// </summary>
        public static async Task OnMapLoadedAsync(NetworkStream stream, long characterId, long mapId)
        {
            if (stream == null) return;
            var raid = GuildRaidManager.RaidOf(characterId);
            foreach (int id in Standing(raid, mapId))
                await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, ConnectionProtocol.Push(Op.Hpm, BuildPlay(id, AtEnd)));
        }

        /// <summary>The raid's red flash for a life lost, on whichever of its maps its members stand.</summary>
        public static async Task LifeLostAsync(RaidInstance raid)
        {
            if (raid == null) return;
            foreach (long member in raid.Members.ToList())
            {
                var session = SessionRegistry.FindByCharacter(member);
                if (session == null || session.State.IsInFight) continue;
                int id = IdOf(session.State.MapId, LifeLost);
                if (id != 0) await session.SendAsync(ConnectionProtocol.Push(Op.Hpm, BuildPlay(id, FromStart)));
            }
        }

        /// <summary>A frame to the raid's members standing on a map, out of a fight.</summary>
        public static async Task TellRaidOnMapAsync(RaidInstance raid, long mapId, byte[] frame)
        {
            foreach (long member in raid.Members.ToList())
            {
                var session = SessionRegistry.FindByCharacter(member);
                if (session != null && session.State.MapId == mapId && !session.State.IsInFight) await session.SendAsync(frame);
            }
        }
    }
}
