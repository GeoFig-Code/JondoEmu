using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jondo.Unity.Launcher;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The guild halls: the seven buildings a guild may make its own, one per nation, and the five
    /// looks of their inside.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE HALLS are the client's (datos/guild_halls_*.json, tools/extract_guild_halls.py): each the
    /// street map its door stands on and the indoor maps of its subarea, the rooms, the Function Room
    /// the street door leads to. Their doors are passages (datos/guild_hall_doors_*.json) that
    /// TeleportManager declares like any other; how they were paired is the tool's to say.
    /// </para>
    /// <para>
    /// A hall is anybody's to walk into. A guild that chooses one (jjz, right 41 «Gestionar el
    /// recibidor de gremio») sees its banners there, in the look it chose (jjw): the theme's staging
    /// on the room's map, «hall lvl N»; anybody else sees «hall neutre», staging 10. The looks open
    /// with the guild's level: the default one at once, one more at each of the client's guild level
    /// rewards «Evolución del recibidor de gremio» (picto guildHall: levels 6, 10, 16 and 20).
    /// </para>
    /// </remarks>
    public static class GuildHalls
    {
        /// <summary>The client's guild right 41, «Gestionar el recibidor de gremio».</summary>
        public const int ManageRight = 41;

        /// <summary>The staging every hall room plays to whoever is not of the hall's guild: «hall neutre».</summary>
        public const int NeutralStaging = 10;

        public sealed record Room(long MapId, string Name);

        public sealed record Hall(int Id, string Name, int SubareaId, long StreetMapId, long FunctionRoom,
                                  long EntranceMapId, int EntranceCellId, IReadOnlyList<Room> Rooms);

        public sealed record Theme(int Id, string Name, int Order);

        private sealed record Document(IReadOnlyList<Hall> Halls, IReadOnlyList<Theme> Themes,
                                       IReadOnlyDictionary<long, Hall> ByRoom, IReadOnlyList<int> ThemeLevels);

        private static Document? _data;
        private static readonly object _lock = new();

        private static Document Data
        {
            get
            {
                if (_data != null) return _data;
                lock (_lock) return _data ??= Load(Paths.GuildHallsJson);
            }
        }

        public static IReadOnlyList<Hall> All => Data.Halls;

        public static IReadOnlyList<Theme> Themes => Data.Themes;

        public static Hall? ById(int id) => Data.Halls.FirstOrDefault(h => h.Id == id);

        /// <summary>The hall a room belongs to, or null for a map that is no hall's room.</summary>
        public static Hall? OfRoom(long mapId) => Data.ByRoom.TryGetValue(mapId, out var hall) ? hall : null;

        /// <summary>The theme every guild has from the start: the first in the client's order.</summary>
        public static int DefaultTheme => Data.Themes.Count == 0 ? 1 : Data.Themes[0].Id;

        /// <summary>The looks a guild of this level may choose, in the client's order.</summary>
        public static IReadOnlyList<int> ThemesAtLevel(int level)
            => Data.Themes.Take(1 + Data.ThemeLevels.Count(at => at <= level)).Select(t => t.Id).ToList();

        /// <summary>The staging a theme's banners are: «hall lvl N», N the theme.</summary>
        public static int StagingOf(int theme) => theme;

        // ─── A guild's hall ─────────────────────────────────────────────────────

        /// <summary>The hall a guild chose and the look it gave it.</summary>
        public sealed record Choice(long GuildId, int HallId, int Theme, long ChangedMs);

        /// <summary>A guild's hall, or null while it has chosen none.</summary>
        public static Choice? ChoiceOf(long guildId)
        {
            using var connection = GuildStore.Open();
            var query = connection.CreateCommand();
            query.CommandText = "SELECT HallId, Theme, ChangedMs FROM GuildHallChoices WHERE GuildId = $g;";
            query.Parameters.AddWithValue("$g", guildId);
            using var reader = query.ExecuteReader();
            return reader.Read() ? new Choice(guildId, reader.GetInt32(0), reader.GetInt32(1), reader.GetInt64(2)) : null;
        }

        /// <summary>The guild makes a hall its own, keeping the look it had.</summary>
        public static Choice Choose(long guildId, int hallId, DateTimeOffset when)
        {
            int theme = ChoiceOf(guildId)?.Theme ?? DefaultTheme;
            var choice = new Choice(guildId, hallId, theme, when.ToUnixTimeMilliseconds());
            Save(choice);
            return choice;
        }

        /// <summary>The guild gives its hall another look. Null when it has no hall.</summary>
        public static Choice? SetTheme(long guildId, int theme)
        {
            var choice = ChoiceOf(guildId);
            if (choice == null) return null;
            choice = choice with { Theme = theme };
            Save(choice);
            return choice;
        }

        private static void Save(Choice choice)
        {
            using var connection = GuildStore.Open();
            var save = connection.CreateCommand();
            save.CommandText = @"
                INSERT INTO GuildHallChoices (GuildId, HallId, Theme, ChangedMs) VALUES ($g, $h, $t, $ms)
                ON CONFLICT (GuildId) DO UPDATE SET HallId = $h, Theme = $t, ChangedMs = $ms;";
            save.Parameters.AddWithValue("$g", choice.GuildId);
            save.Parameters.AddWithValue("$h", choice.HallId);
            save.Parameters.AddWithValue("$t", choice.Theme);
            save.Parameters.AddWithValue("$ms", choice.ChangedMs);
            save.ExecuteNonQuery();
        }

        // ─── What a room tells its visitor ──────────────────────────────────────

        /// <summary>
        /// What a hall room's jss says in its f4 to this character: his guild's hall, with its look
        /// and the looks it may take; or anybody else's, with whether he may choose it.
        /// </summary>
        public static (bool Own, int Theme, IReadOnlyList<int> Themes, bool MayChoose)? Visit(long mapId, long characterId)
        {
            var hall = OfRoom(mapId);
            if (hall == null) return null;
            GuildStore.Guild? guild = null;
            try { guild = characterId == 0 ? null : GuildStore.GuildOf(characterId); }
            catch (Exception ex) { Console.WriteLine($"[Guild halls] Could not read the guild of {characterId}: {ex.Message}"); }
            var choice = guild == null ? null : ChoiceOf(guild.Id);
            if (guild != null && choice != null && choice.HallId == hall.Id)
                return (true, choice.Theme, ThemesAtLevel(guild.Level), false);
            return (false, 0, Array.Empty<int>(), guild != null && GuildStore.HasRight(characterId, ManageRight));
        }

        // ─── Loading ────────────────────────────────────────────────────────────

        private static Document Load(string path)
        {
            var halls = new List<Hall>();
            var themes = new List<Theme>();
            try
            {
                if (!File.Exists(path))
                {
                    Console.WriteLine($"[Guild halls] {path} not found: there are no guild halls.");
                }
                else
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(path));
                    foreach (var t in doc.RootElement.GetProperty("themes").EnumerateArray())
                        themes.Add(new Theme(t.GetProperty("id").GetInt32(), t.GetProperty("name").GetString() ?? "",
                                             t.GetProperty("order").GetInt32()));
                    foreach (var h in doc.RootElement.GetProperty("halls").EnumerateArray())
                    {
                        var rooms = h.GetProperty("rooms").EnumerateArray()
                            .Select(r => new Room(r.GetProperty("mapId").GetInt64(), r.GetProperty("name").GetString() ?? ""))
                            .ToList();
                        long entranceMap = h.GetProperty("functionRoom").GetInt64();
                        int entranceCell = -1;
                        if (h.TryGetProperty("entrance", out var entrance) && entrance.ValueKind == JsonValueKind.Object)
                        {
                            entranceMap = entrance.GetProperty("mapId").GetInt64();
                            entranceCell = entrance.GetProperty("cellId").GetInt32();
                        }
                        halls.Add(new Hall(h.GetProperty("id").GetInt32(), h.GetProperty("name").GetString() ?? "",
                                           h.GetProperty("subareaId").GetInt32(), h.GetProperty("streetMapId").GetInt64(),
                                           h.GetProperty("functionRoom").GetInt64(), entranceMap, entranceCell, rooms));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Guild halls] Could not read {path}: {ex.Message}");
            }

            var byRoom = new Dictionary<long, Hall>();
            foreach (var hall in halls)
                foreach (var room in hall.Rooms) byRoom[room.MapId] = hall;
            themes.Sort((a, b) => a.Order.CompareTo(b.Order));
            return new Document(halls, themes, byRoom, ReadThemeLevels());
        }

        /// <summary>The levels a new look opens at: the client's guild level rewards with the picto guildHall.</summary>
        private static IReadOnlyList<int> ReadThemeLevels()
        {
            var levels = new List<int>();
            try
            {
                string path = Paths.Resolve("guild_level_rewards_3.6.10.11.json");
                if (!File.Exists(path)) return levels;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var reward in doc.RootElement.GetProperty("rewards").EnumerateArray())
                    if (reward.GetProperty("picto").GetString() == "guildHall")
                        levels.Add(reward.GetProperty("level").GetInt32());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Guild halls] Could not read the guild level rewards: {ex.Message}");
            }
            levels.Sort();
            return levels;
        }

        /// <summary>The halls' door passages, as TeleportManager takes them.</summary>
        public static IEnumerable<InteractiveTeleport> Doors()
        {
            string path = Paths.GuildHallDoorsJson;
            var doors = new List<InteractiveTeleport>();
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Guild halls] {path} not found: the halls have no doors.");
                return doors;
            }
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var route in doc.RootElement.GetProperty("routes").EnumerateArray())
                {
                    doors.Add(new InteractiveTeleport
                    {
                        SourceMapId = route.GetProperty("sourceMapId").GetInt64(),
                        ElementId = route.GetProperty("elementId").GetInt32(),
                        SourceCellId = route.GetProperty("sourceCellId").GetInt32(),
                        GfxId = route.GetProperty("gfxId").GetInt32(),
                        InteractiveType = TeleportManager.ReadInteractiveType(route),
                        SkillId = route.GetProperty("skillId").GetInt32(),
                        DestinationMapId = route.GetProperty("destinationMapId").GetInt64(),
                        DestinationCellId = route.GetProperty("destinationCellId").GetInt32(),
                        SourceVersion = route.TryGetProperty("sourceVersion", out var v) ? v.GetString() ?? "" : "",
                        Confidence = route.TryGetProperty("confidence", out var c) ? c.GetString() ?? "" : "",
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Guild halls] Could not read {path}: {ex.Message}");
            }
            return doors;
        }
    }
}
