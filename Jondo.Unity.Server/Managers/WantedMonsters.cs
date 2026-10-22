using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The wanted monsters: the templates with isBounty, 87 of them, Akornaddikt the Squirrel in
    /// Astrub among them. They are not archmonsters -- nobody's rare version -- but they are just
    /// as rare: one per group, one per map, one group in ten when the world is loaded, and one of
    /// each in the world.
    ///
    /// The database ships them in 6.352 groups across 3.724 maps, Akornaddikt in every group of
    /// 4,-18; they are thinned as they are read, like the archmonsters.
    ///
    /// Once killed, one is gone for 3 h ± 15 min, as on the official servers since 3.1. When that
    /// is over, the next group that comes out in its zone brings it, with no draw.
    ///
    /// Having no ordinary version, one that loses its place is taken out of the group -- its id
    /// comes back as 0, which matches no monster --, and one that comes back joins the group
    /// instead of standing in for anybody.
    /// </summary>
    public static class WantedMonsters
    {
        private const int MaxGroupSize = 8;

        private static readonly RareMonsterPool _pool = new RareMonsterPool(TimeSpan.FromHours(3));

        private static readonly HashSet<int> _wanted = new HashSet<int>();

        private static readonly object _gate = new object();

        public static void Initialize(SqliteConnection connection)
        {
            var templates = RareMonsterPool.ReadTemplates(connection);

            lock (_gate)
            {
                _wanted.Clear();
                _pool.Clear();

                foreach (var pair in templates)
                {
                    if (!pair.Value.TryGetProperty("isBounty", out var bounty)) continue;
                    if (!bounty.TryGetInt32(out int b) || b == 0) continue;
                    _wanted.Add(pair.Key);
                    _pool.AddZones(pair.Key, pair.Value);
                }

                Console.WriteLine($"[WantedMonsters] {_wanted.Count} wanted monsters, " +
                                  $"{_pool.ZoneCount} of them with a zone of their own.");
            }
        }

        /// <summary>
        /// Thins the wanted monsters out of one group, and says which one it kept, if any. Every
        /// one that does not get to stay comes back as 0. <paramref name="draw"/> as in
        /// <see cref="Archimonsters.Thin"/>.
        /// </summary>
        public static int Thin(long mapId, long groupId, List<int> ids, bool draw = true)
        {
            lock (_gate)
            {
                int kept = 0;

                for (int i = 0; i < ids.Count; i++)
                {
                    int wanted = ids[i];
                    if (!_wanted.Contains(wanted)) continue;

                    if (kept == 0 && _pool.MayStand(mapId, groupId, wanted, draw))
                    {
                        kept = wanted;
                        _pool.Reserve(mapId, wanted);
                        continue;
                    }

                    ids[i] = 0;
                }

                return kept;
            }
        }

        /// <summary>Lets go of the wanted monster a beaten group was holding.</summary>
        public static void Release(long mapId, IEnumerable<int> members)
        {
            lock (_gate) _pool.Release(mapId, members);
        }

        /// <summary>
        /// A group that comes out in a subarea may bring one of its wanted monsters that is up: it
        /// joins the group, or takes the last place of a full one. <see cref="Thin"/> still decides
        /// whether it stays.
        /// </summary>
        public static void MayComeBack(int subAreaId, List<int> ids)
        {
            if (ids.Count == 0) return;

            lock (_gate)
            {
                foreach (int wanted in _wanted)
                {
                    if (!_pool.IsUp(wanted) || !_pool.BelongsTo(wanted, subAreaId)) continue;
                    if (ids.Count < MaxGroupSize) ids.Add(wanted);
                    else ids[ids.Count - 1] = wanted;
                    return;
                }
            }
        }
    }
}
