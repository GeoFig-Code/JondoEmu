using System;
using System.Linq;
using Jondo.Unity.World.Fights;
using Jondo.Unity.World.Maps;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>Forgelance-only rules measured in the spell-by-spell 3.6.10.10 captures.</summary>
    internal static class ForgelanceCombat
    {
        internal const int Breed = 20;
        internal const int Lance = 7139;
        internal const int Armed = 3360;
        internal const int Disarmed = 3361;
        internal const int NearLance = 3589;
        internal const int ArmedAppearance = 2061;
        internal const int GuardAppearance = 2062;
        internal const int LanceSkin = 3650;

        internal static bool Is(Fighter fighter) => fighter != null && fighter.Breed == Breed;

        internal static bool IsActor(FightInstance fight, Fighter fighter)
            => Is(fighter) || (fighter is { EsInvocado: true, MonsterId: Lance }
                              && Is(fight.Buscar(fighter.Invocador)));

        internal static Fighter OwnLance(FightInstance fight, Fighter owner)
            => fight.Azul.Concat(fight.Rojo).FirstOrDefault(f => f.IsAlive && f.EsInvocado
                && f.MonsterId == Lance && f.Invocador == owner.Id);

        // The aura is a square: the four diagonal cells count as well as the four edge neighbours.
        internal static bool IsNear(Fighter owner, Fighter lance)
        {
            if (owner == null || lance == null || !MapGeometry.IsValid(owner.CellId)
                || !MapGeometry.IsValid(lance.CellId)) return false;
            var (x, y) = MapGeometry.CellToPoint(owner.CellId);
            var (lx, ly) = MapGeometry.CellToPoint(lance.CellId);
            return Math.Max(Math.Abs(x - lx), Math.Abs(y - ly)) <= 1;
        }

        internal static bool IsAura(FightInstance fight, Glifo glyph)
            => glyph.Tipo == 1091 && glyph.HechizoQueLoPuso == 24391
               && IsActor(fight, fight.Buscar(glyph.Dueno));

        internal static int ActionSequence(Fighter fighter)
            => Is(fighter) ? 0 : Network.FightProtocol.ActionSequence;
    }
}
