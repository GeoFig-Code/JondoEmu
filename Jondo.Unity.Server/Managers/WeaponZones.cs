using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Jondo.Unity.World.Maps;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The zone a weapon hits in: its type's, out of datos/weapon_zones.json.
    /// </summary>
    /// <remarks>
    /// A weapon's damage lines all carry the same "C1" and the client does not draw that: it draws
    /// the zone of the weapon's type -- a bow, a sword or a dagger hits one cell, a hammer a cross
    /// around it ("X1,0,10,1"), a staff the bar across the hit ("T1,10,1"), a shovel a cone
    /// ahead ("V1,10,2"), a scythe a half circle ("U1,10,1"), a lance three cells on past the
    /// target ("L3,10,3"). The emulator hit the aimed cell whatever the weapon.
    ///
    /// The weapon never hits whoever wields it. In the Martillo Martirio capture the wielder
    /// stands next to the aimed cell, in the cross, and only the creature on it takes the blow.
    /// </remarks>
    internal static class WeaponZones
    {
        /// <summary>A parsed weapon zone: its shape and size, and the damage it loses per cell.</summary>
        internal readonly record struct WeaponZone(int Shape, int Size, int MinSize, int DecreaseStep, int MaxSteps)
        {
            public static readonly WeaponZone Point = new(Zone.Punto, 0, 0, 0, 0);
        }

        private static readonly Lazy<Dictionary<int, WeaponZone>> _zones = new(Load);

        /// <summary>The zone of a weapon type; a single cell for a type the file does not name.</summary>
        internal static WeaponZone Of(int itemType)
            => _zones.Value.TryGetValue(itemType, out var zone) ? zone : WeaponZone.Point;

        /// <summary>
        /// A rawZone as the client reads it: the shape's letter, then its size; for the shapes that
        /// have a minimum size (C X Q + #) the minimum next; then the damage lost per cell and the
        /// most cells it is lost for. "X1,0,10,1" is a cross of one, from the centre, losing 10%
        /// once; "T1,10,1" a bar of one losing 10% once.
        /// </summary>
        internal static WeaponZone Parse(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return WeaponZone.Point;
            int shape = raw[0];
            var numbers = new List<int>();
            foreach (var part in raw.Substring(1).Split(',', StringSplitOptions.RemoveEmptyEntries))
                numbers.Add(int.TryParse(part, out int n) ? n : 0);

            bool hasMinSize = shape is 'C' or 'X' or 'Q' or '+' or '#';
            int At(int i) => i < numbers.Count ? numbers[i] : 0;
            return numbers.Count switch
            {
                0 => new WeaponZone(shape, 0, 0, 0, 0),
                1 => new WeaponZone(shape, At(0), 0, 0, 0),
                2 => hasMinSize ? new WeaponZone(shape, At(0), At(1), 0, 0)
                                : new WeaponZone(shape, At(0), 0, At(1), 0),
                3 => hasMinSize ? new WeaponZone(shape, At(0), At(1), At(2), 0)
                                : new WeaponZone(shape, At(0), 0, At(1), At(2)),
                _ => new WeaponZone(shape, At(0), At(1), At(2), At(3)),
            };
        }

        private static Dictionary<int, WeaponZone> Load()
        {
            var zones = new Dictionary<int, WeaponZone>();
            string path = Jondo.Unity.Launcher.Paths.WeaponZonesJson;
            try
            {
                if (!File.Exists(path))
                {
                    Console.WriteLine($"[WeaponZones] {Path.GetFileName(path)} is missing; every weapon hits one cell.");
                    return zones;
                }
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var entry in doc.RootElement.EnumerateObject())
                {
                    if (int.TryParse(entry.Name, out int type))
                        zones[type] = Parse(entry.Value.GetString());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WeaponZones] Could not read {Path.GetFileName(path)}: {ex.Message}");
            }
            return zones;
        }
    }
}
