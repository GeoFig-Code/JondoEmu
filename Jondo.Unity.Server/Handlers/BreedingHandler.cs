using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.Protocol;

namespace Jondo.Unity.Server.Handlers
{
    /// <summary>
    /// The breeding window, opened at a paddock's gate.
    /// </summary>
    /// <remarks>
    /// The gate's skill opens the window on its paddock (hsb); the window asks for its content as it
    /// is built (hqx → hql), the paddock tab for the paddocks the breeder may open (hue → hus, hrc),
    /// and each paddock picked for its own (hrl → hrc). What the window does is read off the client:
    /// eqa builds the requests out of its events, eqc handles the answers.
    /// </remarks>
    public static class BreedingHandler
    {
        /// <summary>The paddock each character has the window open on.</summary>
        private static readonly ConcurrentDictionary<long, int> _openOn = new();

        private static async Task WriteAsync(NetworkStream stream, byte[] frame)
            => await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, frame);

        /// <summary>A paddock's gate used: its paddock opens, if the breeder's level reaches it.</summary>
        public static async Task UseGateAsync(NetworkStream stream, int elementId, int skillId)
        {
            var paddock = Breeding.PaddockBySkill(skillId);
            if (paddock == null) return;
            int level = SessionContext.State.JobLevel(Breeding.BreederJob);
            if (!Breeding.Opens(paddock, level))
            {
                await WriteAsync(stream, ConnectionProtocol.Push(Op.Lqn,
                    ConnectionProtocol.BuildInfoMessage(InfoMessages.Warning, InfoMessages.JobLevelTooLow)));
                Console.WriteLine($"[Breeding] {SessionContext.State.CharacterName} is breeder level {level}: " +
                                  $"«{paddock.Name}» asks for {paddock.MinLevel}.");
                return;
            }

            _openOn[SessionContext.State.CharacterId] = paddock.Id;
            await WriteAsync(stream, ConnectionProtocol.Push(Op.Iwn,
                ConnectionProtocol.BuildElementInUse(elementId, skillId, SessionContext.State.CharacterId)));
            await WriteAsync(stream, ConnectionProtocol.Push(Op.Hsb, BreedingProtocol.BuildOpenWindow(paddock.Id)));
            Console.WriteLine($"[Breeding] {SessionContext.State.CharacterName} opens «{paddock.Name}».");
        }

        /// <summary>hqx: the window is built and asks for its content.</summary>
        public static async Task WindowOpenedAsync(NetworkStream stream)
        {
            await WriteAsync(stream, ConnectionProtocol.Push(Op.Hql,
                BreedingProtocol.BuildWindowContent(ContentOf(OpenPaddock()))));
        }

        /// <summary>hue: the paddock tab is shown; it gets the paddocks and the one open.</summary>
        public static async Task PaddockTabShownAsync(NetworkStream stream)
        {
            int level = SessionContext.State.JobLevel(Breeding.BreederJob);
            await WriteAsync(stream, ConnectionProtocol.Push(Op.Hus, BreedingProtocol.BuildAccessiblePaddocks(
                Breeding.Paddocks.Select(p => (p.Id, Breeding.Opens(p, level))))));
            await SendPaddockAsync(stream, OpenPaddock());
        }

        /// <summary>hrl: a paddock picked in the tab.</summary>
        public static async Task PaddockPickedAsync(NetworkStream stream, byte[] frame)
        {
            byte[] payload = ConnectionProtocol.ReadPayload(frame, Op.Hrl);
            if (payload == null) return;
            int picked = BreedingProtocol.ReadPickedPaddock(payload);
            Console.WriteLine($"[Breeding] {SessionContext.State.CharacterName} picks paddock {picked}.");
            var paddock = Breeding.PaddockOf(picked);
            if (paddock == null) return;
            if (!Breeding.Opens(paddock, SessionContext.State.JobLevel(Breeding.BreederJob))) return;
            _openOn[SessionContext.State.CharacterId] = paddock.Id;
            await SendPaddockAsync(stream, paddock.Id);
        }

        /// <summary>hqq: the paddock tab is hidden. Nothing to answer.</summary>
        public static void PaddockTabHidden()
            => Console.WriteLine($"[Breeding] {SessionContext.State.CharacterName} leaves the paddock tab.");

        private static int OpenPaddock()
            => _openOn.TryGetValue(SessionContext.State.CharacterId, out int paddock) ? paddock : 1;

        private static async Task SendPaddockAsync(NetworkStream stream, int paddock)
            => await WriteAsync(stream, ConnectionProtocol.Push(Op.Hrc, BreedingProtocol.BuildPaddock(paddock, ContentOf(paddock))));

        /// <summary>A paddock's content: its gauges empty and off, and no ride yet.</summary>
        private static Pb ContentOf(int paddock)
            => BreedingProtocol.PaddockContent(new int[BreedingProtocol.GaugeCount], Array.Empty<int>());
    }
}
