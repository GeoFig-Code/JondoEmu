using System.Runtime.CompilerServices;
using Jondo.Unity.Launcher.Network;

namespace Jondo.Unity.Tests.Launcher
{
    /// <summary>
    /// The launcher talks to no server in the tests.
    /// </summary>
    /// <remarks>
    /// The window asks on opening, and with a server running on the machine the photo and render
    /// tests sent it the player's stored sessions: a run left the server's console full of
    /// "[Launches] Token … unknown". In CI there is no server, so this is also what CI sees.
    /// </remarks>
    internal static class LauncherOffline
    {
        [ModuleInitializer]
        internal static void NoServer() => ControlClient.Offline = true;
    }
}
