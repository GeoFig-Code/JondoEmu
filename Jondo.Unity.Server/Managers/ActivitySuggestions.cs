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
    /// The guidebook's «Sugerencias»: the activities its cards propose, chosen by the level range,
    /// the category and the area the player picked, and the cards he locked to keep them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client's 1,410 suggestions and their 19 categories (datos/activity_suggestions_*.json,
    /// tools/extract_activity_suggestions.py): a category, a level, a map, a large card or not,
    /// and a date window for the seasonal ones.
    /// </para>
    /// <para>
    /// Read in the client (GameSuggestionTabUI): it asks hmo { 1 level to, 2 cards -- always 50 --,
    /// 3 level from, 4 category or 0, 5 area or 32767 for any }, built from its action cvv; it
    /// draws hmn's two lists of ids, f1 the locked cards and f2 the others (bsy); and a card's lock
    /// sends hmp { 2 the activity }. Which list is the locked one is read off Dofus 2's
    /// ActivitySuggestionsMessage, whose locked ids come first, the same order hmn keeps.
    /// INFERRED: no capture opens the tab. The draw is ours: random among those that fit.
    /// </para>
    /// <para>
    /// The locks are the character's, in world.db:
    /// <code>
    ///   CharacterLockedActivities (CharacterId, ActivityId)
    /// </code>
    /// </para>
    /// </remarks>
    public static class ActivitySuggestions
    {
        public sealed record Suggestion(int Id, int Category, int Level, long Map, bool Large, long StartMs, long EndMs);

        /// <summary>The area id the client sends for «any area».</summary>
        public const int AnyArea = 32767;

        private static IReadOnlyList<Suggestion>? _all;
        private static IReadOnlyDictionary<int, int>? _parentOf;
        private static readonly object _gate = new object();
        private static volatile bool _tableReady;

        public static IReadOnlyList<Suggestion> All
        {
            get
            {
                if (_all != null) return _all;
                lock (_gate)
                {
                    if (_all == null) Read();
                    return _all!;
                }
            }
        }

        private static void Read()
        {
            var suggestions = new List<Suggestion>();
            var parents = new Dictionary<int, int>();
            string path = Paths.ActivitySuggestionsJson;
            if (File.Exists(path))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(path));
                    foreach (var c in doc.RootElement.GetProperty("categories").EnumerateArray())
                        parents[c.GetProperty("id").GetInt32()] = c.GetProperty("parent").GetInt32();
                    foreach (var s in doc.RootElement.GetProperty("suggestions").EnumerateArray())
                        suggestions.Add(new Suggestion(s.GetProperty("id").GetInt32(), s.GetProperty("category").GetInt32(),
                            s.GetProperty("level").GetInt32(), s.GetProperty("map").GetInt64(), s.GetProperty("large").GetBoolean(),
                            s.GetProperty("start").GetInt64(), s.GetProperty("end").GetInt64()));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Suggestions] {Path.GetFileName(path)} could not be read: {ex.Message}");
                }
            }
            _parentOf = parents;
            _all = suggestions;
        }

        /// <summary>Whether a suggestion is of that category or of one under it.</summary>
        public static bool InCategory(Suggestion suggestion, int category)
        {
            if (category == 0) return true;
            if (suggestion.Category == category) return true;
            return _parentOf != null && _parentOf.TryGetValue(suggestion.Category, out int parent) && parent == category;
        }

        /// <summary>
        /// The suggestions that fit the request, the locked ones aside, drawn up to the cards wanted.
        /// </summary>
        public static List<int> Draw(int levelFrom, int levelTo, int category, int area, int cards,
                                     IReadOnlySet<int> locked, Func<long, int> areaOfMap, DateTimeOffset now, Random dice)
        {
            long ms = now.ToUnixTimeMilliseconds();
            if (levelTo < levelFrom) (levelFrom, levelTo) = (levelTo, levelFrom);
            var fitting = All.Where(s => s.Level >= levelFrom && s.Level <= levelTo
                                         && !locked.Contains(s.Id)
                                         && InCategory(s, category)
                                         && (s.StartMs <= 0 || s.StartMs <= ms)
                                         && (s.EndMs <= 0 || ms < s.EndMs)
                                         && (area == AnyArea || areaOfMap(s.Map) == area))
                              .Select(s => s.Id).ToList();
            int room = Math.Max(0, cards - locked.Count);
            return fitting.OrderBy(_ => dice.Next()).Take(room).ToList();
        }

        /// <summary>The cards the character locked.</summary>
        public static HashSet<int> LockedBy(long characterId)
        {
            var locked = new HashSet<int>();
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT ActivityId FROM CharacterLockedActivities WHERE CharacterId = $c;";
                command.Parameters.AddWithValue("$c", characterId);
                using var reader = command.ExecuteReader();
                while (reader.Read()) locked.Add(reader.GetInt32(0));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Suggestions] Could not read {characterId}'s locked cards: {ex.Message}");
            }
            return locked;
        }

        /// <summary>A card's lock: on if <paramref name="locked"/>, off otherwise.</summary>
        public static void SetLocked(long characterId, int activityId, bool locked)
        {
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = locked
                    ? "INSERT OR IGNORE INTO CharacterLockedActivities (CharacterId, ActivityId) VALUES ($c, $a);"
                    : "DELETE FROM CharacterLockedActivities WHERE CharacterId = $c AND ActivityId = $a;";
                command.Parameters.AddWithValue("$c", characterId);
                command.Parameters.AddWithValue("$a", activityId);
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Suggestions] Could not lock {activityId} for {characterId}: {ex.Message}");
            }
        }

        private static void EnsureTable()
        {
            if (_tableReady) return;
            lock (_gate)
            {
                if (_tableReady) return;
                using var connection = Open();
                using var create = connection.CreateCommand();
                create.CommandText = "CREATE TABLE IF NOT EXISTS CharacterLockedActivities (" +
                                     "CharacterId INTEGER NOT NULL, ActivityId INTEGER NOT NULL, PRIMARY KEY (CharacterId, ActivityId));";
                create.ExecuteNonQuery();
                _tableReady = true;
            }
        }

        private static SqliteConnection Open()
        {
            var connection = new SqliteConnection(DatabaseManager.WorldConnectionString);
            connection.Open();
            return connection;
        }
    }
}
