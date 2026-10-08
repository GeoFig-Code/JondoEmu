namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The bits of a spell level's <c>m_flags</c>, the SpellLevels.Flags column: the client's
    /// <c>SpellLevelFlags</c> enum, whose names its metadata lists in this order -- CastInLine,
    /// CastInDiagonal, CastTestLos, NeedFreeCell, NeedTakenCell, NeedFreeTrapCell,
    /// RangeCanBeBoosted, HideEffects, PlayAnimation, NeedVisibleEntity, NeedCellWithoutPortal,
    /// PortalProjectionForbidden, ProportionalDiagonal.
    /// </summary>
    /// <remarks>
    /// The bit of each name is read off the 34,685 levels of the data. The first five match the
    /// columns already split out of it (CastInLine, CastTestLos, NeedFreeCell...). Bit 9 is on
    /// 28,960 levels, so it is PlayAnimation, and bit 8 is on one only, so it is not in the enum.
    /// That leaves the two portal bits, 11 and 12. Bit 11 is on the spells that lay a portal or a
    /// trap: Portal, Errancia, Exilio, Trampa Dimensional, Raratrampa. Bit 12 is on six spells,
    /// and on no Selatrop spell.
    /// </remarks>
    public static class SpellLevelFlags
    {
        /// <summary>The cell it lands on must hold no portal: Portal, Errancia, Exilio.</summary>
        public const int NeedCellWithoutPortal = 1 << 11;

        /// <summary>Aimed at a portal, it does not go through: it is cast at the portal's cell.</summary>
        public const int PortalProjectionForbidden = 1 << 12;
    }
}
