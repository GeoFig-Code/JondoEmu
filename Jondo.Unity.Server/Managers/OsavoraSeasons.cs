using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Jondo.Unity.Launcher;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// Osavora's seasons: Naissance, Chasse and Repos, one a week, and the staging each of the
    /// dimension's maps plays for the season it is in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DofusPourLesNoobs (Quêtes d'Osavora): «une saison par semaine», changing «tous les lundis à
    /// 8 heure», in that order. The 3.6 client plays no season by itself -- no string of its code
    /// names them -- while 146 of the dimension's 149 maps carry sequences named after them
    /// (datos/osavora_seasons_*.json, extract_osavora_seasons.py): the server plays the season's
    /// sequence, as it plays a raid's (hpm).
    /// </para>
    /// <para>
    /// INFERRED: which week is which. The weeks are counted from Monday 21 July 2025, 8:00 in
    /// Paris, the week the 3.2 update opened Osavora, taken to be a Naissance. What the season
    /// changes besides the maps' look -- the monsters' passives and spread, the weekly quests --
    /// is not done: the guide says what, not how.
    /// </para>
    /// </remarks>
    public static class OsavoraSeasons
    {
        public enum Season { Naissance, Chasse, Repos }

        /// <summary>The first Naissance, on Paris's clock: Monday 21 July 2025, 8:00.</summary>
        public static readonly DateTime Anchor = new(2025, 7, 21, 8, 0, 0);

        private static readonly TimeZoneInfo Paris = FindParis();

        private static TimeZoneInfo FindParis()
        {
            foreach (string id in new[] { "Europe/Paris", "Romance Standard Time" })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            return TimeZoneInfo.Utc;
        }

        /// <summary>The Monday 8:00 in Paris the week of that moment started on, on Paris's clock.</summary>
        private static DateTime WeekStart(DateTimeOffset moment)
        {
            var local = TimeZoneInfo.ConvertTime(moment, Paris).DateTime;
            var start = local.Date.AddDays(-(((int)local.DayOfWeek + 6) % 7)).AddHours(8);
            return local < start ? start.AddDays(-7) : start;
        }

        /// <summary>The season at a moment.</summary>
        public static Season At(DateTimeOffset moment)
        {
            long weeks = (long)Math.Round((WeekStart(moment) - Anchor).TotalDays / 7);
            return (Season)(int)(((weeks % 3) + 3) % 3);
        }

        /// <summary>When the season after the one at that moment starts: the next Monday, 8:00 in Paris.</summary>
        public static DateTimeOffset NextChange(DateTimeOffset moment)
        {
            var next = WeekStart(moment).AddDays(7);
            return new DateTimeOffset(next, Paris.GetUtcOffset(next));
        }

        private static Dictionary<long, Dictionary<string, int>>? _maps;
        private static readonly object _lock = new();

        private static Dictionary<long, Dictionary<string, int>> Maps
        {
            get
            {
                if (_maps != null) return _maps;
                lock (_lock)
                {
                    _maps ??= Load(Paths.OsavoraSeasonsJson);
                    return _maps;
                }
            }
        }

        /// <summary>Whether a map changes with the seasons.</summary>
        public static bool IsSeasonal(long mapId) => Maps.ContainsKey(mapId);

        /// <summary>The map's sequence for a season, or 0 when it has none.</summary>
        public static int StagingOn(long mapId, Season season)
            => Maps.TryGetValue(mapId, out var named) && named.TryGetValue(season.ToString(), out int id) ? id : 0;

        private static Dictionary<long, Dictionary<string, int>> Load(string path)
        {
            var maps = new Dictionary<long, Dictionary<string, int>>();
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Osavora] {path} not found: the dimension keeps one look.");
                return maps;
            }
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var map in json.RootElement.GetProperty("maps").EnumerateObject())
                {
                    if (!long.TryParse(map.Name, out long mapId)) continue;
                    var named = new Dictionary<string, int>();
                    foreach (var sequence in map.Value.EnumerateObject()) named[sequence.Name] = sequence.Value.GetInt32();
                    maps[mapId] = named;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Osavora] Could not read {Path.GetFileName(path)}: {ex.Message}");
            }
            Console.WriteLine($"[Osavora] {maps.Count} maps change with the seasons.");
            return maps;
        }
    }
}
