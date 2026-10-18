using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Jondo.Unity.Tests
{
    /// <summary>
    /// With JONDO_FRAME_TAP set to a file, every frame the server writes during the tests goes
    /// there in the traffic log's shape, for tools/auditar_protocolo.py to read against the
    /// client's schema. Unset, nothing is tapped.
    /// </summary>
    internal static class FrameTap
    {
        private static readonly object Gate = new();

        [ModuleInitializer]
        internal static void Open()
        {
            string? path = Environment.GetEnvironmentVariable("JONDO_FRAME_TAP");
            if (string.IsNullOrWhiteSpace(path)) return;
            Jondo.Protocol.NetworkMessage.FrameTap = payload =>
            {
                string line = $"[00:00:00.000] S->C ({payload.Length} bytes)\nHex: {BitConverter.ToString(payload)}\n";
                lock (Gate) File.AppendAllText(path, line);
            };
        }
    }
}
