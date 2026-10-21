using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// Which monsters are archmonsters, where each one belongs, and how thin they have to be
    /// spread.
    ///
    /// A monster is an archmonster when it is somebody else's `correspondingMiniBossId`: the
    /// client's data pairs every ordinary monster with the rare version of itself. There are 306
    /// of them and all 306 declare the subareas they belong to.
    ///
    /// What the world looked like before this existed:
    ///
    ///   39,9 % of the groups had at least one       35.518 placed
    ///   up to 8 in a single group                   5.802 maps with more than one
    ///
    /// The rules, all four of them:
    ///
    ///   - one per group at most
    ///   - one per map at most
    ///   - one in ten groups, not four in ten, when the world is loaded
    ///   - and one of each per zone: if the Dark Treechnid is standing on a map of its subarea, it
    ///     is on no other map of that subarea until something kills it
    ///
    /// Once killed, it is gone for 2 h ± 15 min, as on the official servers since 3.1. When that is
    /// over, the next group that comes out in its zone brings it, with no draw.
    ///
    /// The draw is deterministic — seeded from the group's own id — so a map looks the same after
    /// a restart as it did before. Nothing is rewritten in the database: the 38.744 groups it ships
    /// with are left alone and thinned as they are read.
    ///
    /// An archmonster that loses its place is not deleted but demoted: it is swapped for the
    /// ordinary monster it is the rare version of, so the group keeps its size and its level.
    ///
    /// The wanted monsters are not archmonsters: they are <see cref="WantedMonsters"/>.
    /// </summary>
    public static class Archimonsters
    {
        private static readonly RareMonsterPool _pool = new RareMonsterPool(TimeSpan.FromHours(2));

        /// <summary>archmonster id -> the ordinary monster it is the rare version of.</summary>
        private static readonly Dictionary<int, int> _ordinary = new Dictionary<int, int>();

        /// <summary>
        /// The groups are thinned at loading, but also when a beaten group is replaced and when a map
        /// is filled on the fly, from the sessions' own threads.
        /// </summary>
        private static readonly object _gate = new object();

        public static void Initialize(SqliteConnection connection)
        {
            var templates = RareMonsterPool.ReadTemplates(connection);

            lock (_gate)
            {
                _ordinary.Clear();
                _pool.Clear();

                // Who is the rare version of whom.
                foreach (var pair in templates)
                {
                    if (!pair.Value.TryGetProperty("correspondingMiniBossId", out var mini)) continue;
                    if (!mini.TryGetInt32(out int rare) || rare == 0) continue;
                    _ordinary[rare] = pair.Key;
                }

                // And where each of them belongs.
                foreach (int rare in _ordinary.Keys)
                {
                    if (templates.TryGetValue(rare, out var template)) _pool.AddZones(rare, template);
                }

                Console.WriteLine($"[Archimonsters] {_ordinary.Count} archmonsters, " +
                                  $"{_pool.ZoneCount} of them with a zone of their own.");
            }
        }

        /// <summary>
        /// Thins the archmonsters out of one group, and says which one it kept, if any.
        ///
        /// <paramref name="ids"/> is the group as the database has it and comes back trimmed:
        /// every archmonster that does not get to stay is swapped for its ordinary version.
        /// <paramref name="draw"/> is the one-in-ten of loading. A group that comes out later does
        /// not draw: one that is up stands in it.
        /// </summary>
        public static int Thin(long mapId, long groupId, List<int> ids, bool draw = true)
        {
            lock (_gate)
            {
                int kept = 0;

                for (int i = 0; i < ids.Count; i++)
                {
                    int rare = ids[i];
                    if (!_ordinary.TryGetValue(rare, out int ordinary)) continue;

                    if (kept == 0 && _pool.MayStand(mapId, groupId, rare, draw))
                    {
                        kept = rare;
                        _pool.Reserve(mapId, rare);
                        continue;
                    }

                    ids[i] = ordinary;
                }

                return kept;
            }
        }

        /// <summary>
        /// Lets go of the archmonster a beaten group was holding, so its zone can have it again
        /// somewhere else once its respawn time is over.
        /// </summary>
        public static void Release(long mapId, IEnumerable<int> members)
        {
            lock (_gate) _pool.Release(mapId, members);
        }

        /// <summary>
        /// A group that comes out in a subarea may bring back one of its archmonsters that is up
        /// -- never placed, or beaten and its respawn time over --, in the place of the ordinary
        /// monster it is the rare version of. <see cref="Thin"/> still decides whether it stays.
        /// </summary>
        public static void MayComeBack(int subAreaId, List<int> ids)
        {
            lock (_gate)
            {
                foreach (var pair in _ordinary)
                {
                    if (!_pool.IsUp(pair.Key) || !_pool.BelongsTo(pair.Key, subAreaId)) continue;
                    int i = ids.IndexOf(pair.Value);
                    if (i < 0) continue;
                    ids[i] = pair.Key;
                    return;
                }
            }
        }
    }
}
