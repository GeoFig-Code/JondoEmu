using System.Collections.Generic;
using Jondo.Unity.Launcher;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The spells only whoever administers the server has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// They are not made up: they are in the client's own data, with their name, their icon and their
    /// description, and they are the ones Ankama reserves for its team. Here only who they are declared
    /// to is decided, which is what the client cannot know by itself.
    /// </para>
    /// <para>
    /// Doom de Masas comes from the type «[!] Cheat» (2877); the rest are the whole of the type
    /// «[!] Sorts admins» (3725), the team's own set, each with one grade and one AP: Alterabuso,
    /// Doom, Superposicionamiento, Pasaturnonoob Infinito, Mindslaver, Invulnerabilidad Infinita,
    /// Furor de Soon, Desembrujo and Papirotazo. Left out of that type: «A» and «B», two test
    /// placeholders whose name and description are the one letter, and 30196, Superposicionamiento's
    /// triggered half, which its spell casts. The other «[!] Cheat» spells are Ankama's debug
    /// versions of these same ones, and «Cambio de Equipo» needs effect 3107, which the engine does not
    /// have.
    /// </para>
    /// <para>
    /// The check is ALWAYS against the database and by account, never against anything the client sends,
    /// the same as the chat commands'.
    /// </para>
    /// </remarks>
    public static class AdminSpells
    {
        /// <summary>Doom de Masas: kills everything the area catches.</summary>
        /// <remarks>
        /// Taken from the client's catalogue: name «Doom de Masas», adminName «Doom de masse», a single
        /// grade, 1 AP, range 0, and two effects -- 141 «Mata al objetivo» (kills the target) over the
        /// area and 120, which gives back the AP spent. So it can be chained without running out of
        /// points, which is exactly what is needed to skip a test fight.
        /// </remarks>
        public const int DoomDeMasas = 3450;

        /// <summary>Its only grade.</summary>
        public const int GradoDeDoom = 1;

        /// <summary>
        /// Every administration spell with its grade: Doom de Masas, then the «[!] Sorts admins».
        /// </summary>
        public static readonly IReadOnlyList<(int Spell, int Grade)> Granted = new[]
        {
            (DoomDeMasas, GradoDeDoom),
            (30040, 1),   // Alterabuso: +AP, +MP, +range
            (30043, 1),   // Doom, on one target
            (30044, 1),   // Superposicionamiento: puts a character or monster on the cell aimed at
            (30045, 1),   // Pasaturnonoob Infinito (On/Off): the target passes its turns
            (30046, 1),   // Mindslaver (On/Off): takes control of the target
            (30047, 1),   // Invulnerabilidad Infinita (On/Off)
            (30048, 1),   // Furor de Soon: lowers the target's life without killing it
            (30049, 1),   // Desembrujo: effects one turn shorter
            (30092, 1),   // Papirotazo: 1 damage, as often as there are AP
        };

        /// <summary>The role from which it is declared.</summary>
        public const int HaceFalta = Roles.Administrador;

        /// <summary>Are the administration spells declared to this account?</summary>
        public static bool Para(long accountId)
        {
            if (accountId <= 0) return false;
            return Roles.AlMenos(DatabaseManager.GetAccountRole(accountId), HaceFalta);
        }
    }
}
