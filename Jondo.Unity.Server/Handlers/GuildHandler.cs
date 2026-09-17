using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.Protocol;

namespace Jondo.Unity.Server.Handlers
{
    /// <summary>
    /// El gremio: crearlo, mostrarlo y abandonarlo. La base sobre la que irán la tienda, el cofre
    /// y las raids.
    ///
    /// Medido en las 12 capturas de Gremio/. Al crear (jjg) el servidor real contesta con el
    /// gremio al que ahora perteneces (jgw), sus rangos (jco), su cabecera (jhh) y tu ficha de
    /// miembro (jgu); al abrir la ventana repite jco, jgu y jhh; al salir (jho) confirma con khj.
    /// Lo que aún no se toca -candidaturas, permisos, contribuciones- se deja dicho.
    /// </summary>
    public static class GuildHandler
    {
        private static async Task WriteAsync(NetworkStream stream, byte[] frame)
            => await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, frame);

        /// <summary>
        /// Crear un gremio (jjg): f1 el emblema {símbolo, color símbolo, fondo, color fondo}, f2
        /// el nombre. Se guarda, el fundador entra de rango 1 y se le manda todo lo suyo.
        /// </summary>
        /// <remarks>
        /// El gremialogema -el objeto que se gasta al crear- no se comprueba aquí todavía: en las
        /// capturas se compra a un PNJ aparte, y sin el inventario de objetos consumibles del
        /// gremio montado no hay nada que descontar. Queda dicho para cuando lo haya.
        /// </remarks>
        public static async Task CreateAsync(NetworkStream stream, byte[] frame)
        {
            byte[] jjg = ConnectionProtocol.ReadPayload(frame, Op.Jjg);
            if (jjg == null) return;

            long who = SessionContext.State.CharacterId;
            if (who == 0 || GuildStore.GuildOf(who) != null) return;   // ya tiene gremio: no se crea otro

            string name = "";
            int symbol = 0, symbolColor = 0, background = 0, symbolRgb = 0;
            foreach (var field in ProtoMessage.Parse(jjg).Fields)
            {
                if (field.FieldNumber == 2 && field.WireType == 2)
                {
                    name = System.Text.Encoding.UTF8.GetString(field.BytesValue);
                }
                else if (field.FieldNumber == 1 && field.WireType == 2)
                {
                    foreach (var e in ProtoMessage.Parse(field.BytesValue).Fields)
                    {
                        if (e.WireType != 0) continue;
                        if (e.FieldNumber == 1) symbol = (int)e.VarIntValue;
                        else if (e.FieldNumber == 2) symbolColor = (int)e.VarIntValue;
                        else if (e.FieldNumber == 3) background = (int)e.VarIntValue;
                        else if (e.FieldNumber == 5) symbolRgb = (int)e.VarIntValue;
                    }
                }
            }
            if (string.IsNullOrWhiteSpace(name)) return;

            var guild = GuildStore.Create(who, name, symbol, symbolColor, background, symbolRgb);
            await SendGuildToOwnerAsync(stream, guild, who);
        }

        /// <summary>
        /// Le manda a un personaje todo lo de su gremio: pertenencia, rangos, cabecera y la lista
        /// de miembros. Se usa al crear, al abrir la ventana y al entrar al mundo.
        /// </summary>
        public static async Task SendGuildToOwnerAsync(NetworkStream stream, GuildStore.Guild guild, long characterId)
        {
            int rank = GuildStore.RankOf(characterId);
            var members = GuildStore.Members(guild.Id);

            await WriteAsync(stream, ConnectionProtocol.Push(Op.Jgw,
                GuildProtocol.BuildGuildJoined(guild, rank)));
            await WriteAsync(stream, ConnectionProtocol.Push(Op.Jco, GuildProtocol.BuildDefaultRanks()));
            foreach (var frame in MemberFrames(members))
                await WriteAsync(stream, frame);
            await WriteAsync(stream, ConnectionProtocol.Push(Op.Jhh,
                GuildProtocol.BuildGuildInfo(guild, members.Count)));
        }

        /// <summary>Una trama jgu por miembro, con el nombre, el nivel y la cuenta de cada uno.</summary>
        public static List<byte[]> MemberFrames(List<GuildStore.Member> members)
        {
            var fuera = new List<byte[]>();
            foreach (var member in members)
            {
                var character = DatabaseManager.GetCharacterById(member.CharacterId);
                if (character == null) continue;
                fuera.Add(ConnectionProtocol.Push(Op.Jgu,
                    GuildProtocol.BuildMember(member, character.Name, character.Level, character.AccountId)));
            }
            return fuera;
        }

        /// <summary>
        /// Abrir la ventana de gremio (jml / jii). El cliente la pide con una ráfaga de mensajes
        /// vacíos; el servidor real responde con los rangos, los miembros y la cabecera. Si el
        /// personaje no tiene gremio, no hay nada que mandar.
        /// </summary>
        public static async Task OpenWindowAsync(NetworkStream stream, byte[] frame)
        {
            long who = SessionContext.State.CharacterId;
            var guild = who == 0 ? null : GuildStore.GuildOf(who);
            if (guild == null) return;
            await SendGuildToOwnerAsync(stream, guild, who);
        }

        /// <summary>
        /// Abandonar el gremio (jho): f1 el personaje. Se saca y se confirma con khj, que es como
        /// el cliente vacía la ventana. Medido en «salir de mi gremio».
        /// </summary>
        public static async Task LeaveAsync(NetworkStream stream, byte[] frame)
        {
            long who = SessionContext.State.CharacterId;
            if (who == 0) return;
            var guild = GuildStore.GuildOf(who);
            int slot = GuildStore.RankOf(who);
            if (GuildStore.Leave(who) == null) return;
            await WriteAsync(stream, ConnectionProtocol.Push(Op.Khj, GuildProtocol.BuildMemberGone(slot)));
            if (guild != null) await RefreshEveryoneAsync(guild, who);
        }

        /// <summary>
        /// Le pone al día la ventana a todos los del gremio que estén conectados: la lista de
        /// miembros y la cabecera con cuántos son. Es lo que hace falta cuando entra o sale uno.
        /// </summary>
        private static async Task RefreshEveryoneAsync(GuildStore.Guild guild, long exceptCharacter = 0)
        {
            var members = GuildStore.Members(guild.Id);
            var frames = MemberFrames(members);
            byte[] header = ConnectionProtocol.Push(Op.Jhh, GuildProtocol.BuildGuildInfo(guild, members.Count));

            foreach (var member in members)
            {
                if (member.CharacterId == exceptCharacter) continue;
                var session = SessionRegistry.FindByCharacter(member.CharacterId);
                if (session == null) continue;
                foreach (var frame in frames) await session.SendAsync(frame);
                await session.SendAsync(header);
            }
        }

        // ─── La tienda del gremio ───────────────────────────────────────────────

        /// <summary>
        /// Abrir la tienda (jki) y contestar con sus cinco oráculos (jkh), con el precio ya
        /// multiplicado por las cuentas del gremio.
        /// </summary>
        public static async Task OpenShopAsync(NetworkStream stream, byte[] frame)
        {
            long who = SessionContext.State.CharacterId;
            var guild = who == 0 ? null : GuildStore.GuildOf(who);
            if (guild == null) return;
            await WriteAsync(stream, ConnectionProtocol.Push(Op.Jkh, GuildProtocol.BuildShop(AccountsIn(guild))));
        }

        /// <summary>
        /// Las cuentas distintas que tiene el gremio, que es por lo que se multiplica el precio:
        /// cuatro cuentas, precios por cuatro. Dos personajes de la misma cuenta cuentan una vez.
        /// </summary>
        private static int AccountsIn(GuildStore.Guild guild)
        {
            var cuentas = new HashSet<long>();
            foreach (var member in GuildStore.Members(guild.Id))
            {
                var character = DatabaseManager.GetCharacterById(member.CharacterId);
                if (character != null) cuentas.Add(character.AccountId);
            }
            return cuentas.Count < 1 ? 1 : cuentas.Count;
        }

        /// <summary>
        /// Comprar un oráculo (jkw): si llegan los kamas de gremio se apunta y se contesta con el
        /// acuse, los kamas que quedan y lo que falta por activar; si no llegan, el acuse VACÍO,
        /// que es lo que manda el servidor real cuando lo rechaza.
        /// </summary>
        public static async Task BuyOracleAsync(NetworkStream stream, byte[] frame)
        {
            byte[] jkw = ConnectionProtocol.ReadPayload(frame, Op.Jkw);
            if (jkw == null) return;
            int oracle = (int)FieldValue(jkw, 1);

            long who = SessionContext.State.CharacterId;
            var guild = who == 0 ? null : GuildStore.GuildOf(who);
            if (guild == null || GuildOracles.Of(oracle) == null)
            {
                await WriteAsync(stream, ConnectionProtocol.Push(Op.Jkj, GuildProtocol.BuildShopRefused()));
                return;
            }

            int price = GuildOracles.PriceFor(oracle, AccountsIn(guild));
            if (!GuildStore.SpendGuildKamas(guild.Id, price))
            {
                await WriteAsync(stream, ConnectionProtocol.Push(Op.Jkj, GuildProtocol.BuildShopRefused()));
                return;
            }

            var deadline = DateTimeOffset.UtcNow.AddHours(GuildOracles.HoursToActivate);
            GuildStore.BuyOracle(guild.Id, oracle, deadline);
            long left = GuildStore.GuildOf(who).GuildKamas;

            await WriteAsync(stream, ConnectionProtocol.Push(Op.Jkj, GuildProtocol.BuildShopBought(oracle)));
            await WriteAsync(stream, ConnectionProtocol.Push(Op.Jia, GuildProtocol.BuildGuildKamas(left)));
            await WriteAsync(stream, ConnectionProtocol.Push(Op.Jkv,
                GuildProtocol.BuildPendingOracle(oracle, GuildStore.OracleDeadline(guild.Id, oracle))));
        }

        /// <summary>
        /// Activar un oráculo comprado (jky): el acuse y la alteración, que dura dos horas. Sólo
        /// si el gremio lo tiene comprado y el plazo no se ha pasado.
        /// </summary>
        public static async Task ActivateOracleAsync(NetworkStream stream, byte[] frame)
        {
            byte[] jky = ConnectionProtocol.ReadPayload(frame, Op.Jky);
            if (jky == null) return;
            int oracle = (int)FieldValue(jky, 1);

            long who = SessionContext.State.CharacterId;
            var guild = who == 0 ? null : GuildStore.GuildOf(who);
            var catalogue = GuildOracles.Of(oracle);
            if (guild == null || catalogue == null) return;

            string deadline = GuildStore.OracleDeadline(guild.Id, oracle);
            if (deadline == null) return;
            if (DateTimeOffset.TryParse(deadline, null, System.Globalization.DateTimeStyles.AdjustToUniversal,
                                        out var cuando) && cuando < DateTimeOffset.UtcNow)
            {
                return;   // se pasó el plazo de un día
            }

            long from = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long to = DateTimeOffset.UtcNow.AddHours(GuildOracles.HoursActive).ToUnixTimeMilliseconds();

            await WriteAsync(stream, ConnectionProtocol.Push(Op.Jkx, GuildProtocol.BuildOracleActivated(oracle)));
            await WriteAsync(stream, ConnectionProtocol.Push(Op.Lzs,
                GuildProtocol.BuildAlteration(catalogue.Alteration, from, to)));
        }

        // ─── Contribuir ─────────────────────────────────────────────────────────

        /// <summary>
        /// Contribuir (jlb): diez mil kamas del personaje por diez de gremio, cinco veces por
        /// semana como mucho. Se contesta con la contribución hecha, los kamas del gremio y los
        /// del personaje.
        /// </summary>
        public static async Task ContributeAsync(NetworkStream stream, byte[] frame)
        {
            long who = SessionContext.State.CharacterId;
            var guild = who == 0 ? null : GuildStore.GuildOf(who);
            if (guild == null) return;
            if (GameState.Kamas < GuildStore.ContributionKamas) return;

            int left = GuildStore.Contribute(who, guild.Id);
            if (left < 0) return;   // ya no le quedaban esta semana

            GameState.Kamas -= GuildStore.ContributionKamas;
            DatabaseManager.SaveCurrentCharacter();

            await WriteAsync(stream, ConnectionProtocol.Push(Op.Jle,
                GuildProtocol.BuildContribution(GuildStore.ContributionKamas, left)));
            await WriteAsync(stream, ConnectionProtocol.Push(Op.Jia,
                GuildProtocol.BuildGuildKamas(GuildStore.GuildOf(who).GuildKamas)));
            await WriteAsync(stream, ConnectionProtocol.Push(Op.Ivf,
                ConnectionProtocol.BuildKamas(GameState.Kamas)));
        }

        // ─── Candidaturas ───────────────────────────────────────────────────────

        /// <summary>Lo que hace falta de cada candidato para armar su bloque.</summary>
        private static List<(GuildStore.Application, string, int, long, string, string)> Detailed(
            IEnumerable<GuildStore.Application> applications)
        {
            var fuera = new List<(GuildStore.Application, string, int, long, string, string)>();
            foreach (var application in applications)
            {
                var character = DatabaseManager.GetCharacterById(application.CharacterId);
                if (character == null) continue;
                fuera.Add((application, character.Name, character.Level, character.AccountId, character.Name, ""));
            }
            return fuera;
        }

        /// <summary>La pestaña de candidaturas (jlx/jml → jmf).</summary>
        public static async Task ApplicationsAsync(NetworkStream stream, byte[] frame)
        {
            long who = SessionContext.State.CharacterId;
            var guild = who == 0 ? null : GuildStore.GuildOf(who);
            if (guild == null) return;
            byte[] jlx = ConnectionProtocol.ReadPayload(frame, Op.Jlx);
            int tab = jlx == null ? ApplicationsTab : (int)FieldValue(jlx, 1);
            await WriteAsync(stream, ConnectionProtocol.Push(Op.Jmf,
                GuildProtocol.BuildApplications(tab, Detailed(GuildStore.Applications(guild.Id)))));
        }

        /// <summary>La pestaña que pide el cliente al abrir las candidaturas, medida en la captura.</summary>
        public const int ApplicationsTab = 8;

        /// <summary>Ver una candidatura (jlt → jly).</summary>
        public static async Task ApplicationDetailAsync(NetworkStream stream, byte[] frame)
        {
            byte[] jlt = ConnectionProtocol.ReadPayload(frame, Op.Jlt);
            if (jlt == null) return;
            long applicant = FieldValue(jlt, 2);

            long who = SessionContext.State.CharacterId;
            var guild = who == 0 ? null : GuildStore.GuildOf(who);
            if (guild == null) return;

            var application = GuildStore.ApplicationOf(guild.Id, applicant);
            if (application == null) return;
            var detailed = Detailed(new[] { application });
            if (detailed.Count == 0) return;
            var (one, name, level, accountId, nick, tag) = detailed[0];

            await WriteAsync(stream, ConnectionProtocol.Push(Op.Jly,
                GuildProtocol.BuildApplication(one, name, level, accountId, nick, tag, who)));
        }

        /// <summary>
        /// Aceptar una candidatura (jjn): el candidato entra de rango 4 y al que la acepta le
        /// llegan los dos avisos que salen en la captura, el de aceptada y el de que ya está
        /// dentro. Al que entra se le manda su gremio entero.
        /// </summary>
        public static async Task AcceptApplicationAsync(NetworkStream stream, byte[] frame)
        {
            byte[] jjn = ConnectionProtocol.ReadPayload(frame, Op.Jjn);
            if (jjn == null) return;
            long applicant = FieldValue(jjn, 2);

            long who = SessionContext.State.CharacterId;
            var guild = who == 0 ? null : GuildStore.GuildOf(who);
            if (guild == null) return;
            if (GuildStore.ApplicationOf(guild.Id, applicant) == null) return;
            if (GuildStore.GuildOf(applicant) != null) return;   // ya está en otro

            var character = DatabaseManager.GetCharacterById(applicant);
            if (character == null) return;

            await WriteAsync(stream, ConnectionProtocol.Push(Op.Jin,
                GuildProtocol.BuildApplicationStatus(character.Name, applicant, GuildProtocol.StatusAccepted)));

            GuildStore.Join(applicant, guild.Id);

            await WriteAsync(stream, ConnectionProtocol.Push(Op.Jin,
                GuildProtocol.BuildApplicationStatus(character.Name, applicant, GuildProtocol.StatusJoined)));

            await SendGuildToNewMemberAsync(applicant, guild);
            await RefreshEveryoneAsync(guild, applicant);
        }

        // ─── Invitaciones ───────────────────────────────────────────────────────

        /// <summary>
        /// Invitar a alguien. El botón del cliente no aparece en ninguna captura -las que hay son
        /// del lado de quien la recibe-, así que de momento se dispara desde el comando de chat
        /// y lo que viaja es lo medido: al invitado le llega el jiq con el gremio y el nombre de
        /// quien invita, y él contesta con el jiz.
        /// </summary>
        public static async Task<string> InviteAsync(long inviterCharacterId, string targetName)
        {
            var guild = GuildStore.GuildOf(inviterCharacterId);
            if (guild == null) return "guild.invite.noguild";

            var session = SessionRegistry.FindByName(targetName);
            if (session == null) return "guild.invite.notfound";
            if (GuildStore.GuildOf(session.CharacterId) != null) return "guild.invite.hasguild";

            var inviter = DatabaseManager.GetCharacterById(inviterCharacterId);
            await session.SendAsync(ConnectionProtocol.Push(Op.Jiq,
                GuildProtocol.BuildInvitation(guild, inviter?.Name ?? "")));
            _invitations[session.CharacterId] = guild.Id;
            return null;
        }

        /// <summary>A qué gremio se ha invitado a cada personaje, mientras no conteste.</summary>
        private static readonly Dictionary<long, long> _invitations = new();

        /// <summary>
        /// La respuesta a la invitación (jiz): vacío la rechaza, f1 = 1 la acepta. Medido en la
        /// captura de recibirla, donde al aceptar llegan el gremio entero y el jij.
        /// </summary>
        public static async Task AnswerInvitationAsync(NetworkStream stream, byte[] frame)
        {
            long who = SessionContext.State.CharacterId;
            if (who == 0 || !_invitations.TryGetValue(who, out long guildId)) return;

            byte[] jiz = ConnectionProtocol.ReadPayload(frame, Op.Jiz);
            bool accepts = jiz != null && FieldValue(jiz, 1) == 1;
            _invitations.Remove(who);
            if (!accepts) return;
            if (GuildStore.GuildOf(who) != null) return;

            GuildStore.Join(who, guildId);
            var guild = GuildStore.GuildOf(who);
            if (guild == null) return;

            await SendGuildToOwnerAsync(stream, guild, who);
            await WriteAsync(stream, ConnectionProtocol.Push(Op.Jij, GuildProtocol.BuildJoinDone()));
            await RefreshEveryoneAsync(guild, who);
        }

        /// <summary>Le manda el gremio entero a quien acaba de entrar, si está conectado.</summary>
        private static async Task SendGuildToNewMemberAsync(long characterId, GuildStore.Guild guild)
        {
            var session = SessionRegistry.FindByCharacter(characterId);
            if (session == null) return;
            int rank = GuildStore.RankOf(characterId);
            var members = GuildStore.Members(guild.Id);

            await session.SendAsync(ConnectionProtocol.Push(Op.Jgw, GuildProtocol.BuildGuildJoined(guild, rank)));
            await session.SendAsync(ConnectionProtocol.Push(Op.Jco, GuildProtocol.BuildDefaultRanks()));
            foreach (var frame in MemberFrames(members)) await session.SendAsync(frame);
            await session.SendAsync(ConnectionProtocol.Push(Op.Jhh,
                GuildProtocol.BuildGuildInfo(guild, members.Count)));
            await session.SendAsync(ConnectionProtocol.Push(Op.Jij, GuildProtocol.BuildJoinDone()));
        }

        /// <summary>
        /// Echar una candidatura a un gremio. Como con la invitación, el botón del cliente no
        /// está medido: se manda desde el comando de chat y lo que sí viaja medido es el aviso
        /// al gremio (jma).
        /// </summary>
        public static async Task<string> ApplyAsync(long characterId, string guildName, string message)
        {
            if (GuildStore.GuildOf(characterId) != null) return "guild.apply.hasguild";
            var guild = GuildStore.ByName(guildName);
            if (guild == null) return "guild.apply.notfound";

            var character = DatabaseManager.GetCharacterById(characterId);
            GuildStore.Apply(characterId, guild.Id, message ?? "");

            byte[] aviso = ConnectionProtocol.Push(Op.Jma,
                GuildProtocol.BuildApplicationArrived(characterId, character?.Name ?? ""));
            foreach (var member in GuildStore.Members(guild.Id))
            {
                var session = SessionRegistry.FindByCharacter(member.CharacterId);
                if (session != null) await session.SendAsync(aviso);
            }
            return null;
        }

        /// <summary>El primer varint de un campo, o cero.</summary>
        private static long FieldValue(byte[] payload, int number)
        {
            foreach (var field in ProtoMessage.Parse(payload).Fields)
            {
                if (field.FieldNumber == number && field.WireType == 0) return field.VarIntValue;
            }
            return 0;
        }
    }
}
