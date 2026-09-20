using System.Collections.Generic;
using Jondo.Unity.Server.Managers;

namespace Jondo.Unity.Server.Network
{
    /// <summary>
    /// Las tramas del gremio, cada una medida en las 12 capturas de Gremio/.
    ///
    /// Lo que va byte a byte contra la captura -el gremio al que se pertenece (jgw), la cabecera
    /// de su ventana (jhh) y los cuatro rangos por defecto (jco)- lleva su test. La lista de
    /// miembros (jgu) se arma con los campos que sí se entienden y unos pocos constantes que se
    /// copian de la captura del fundador sin saber qué significan; van marcados donde toca.
    /// </summary>
    public static class GuildProtocol
    {
        /// <summary>
        /// El bloque del gremio que comparten el jgw, el jhe y el actor del mapa: el emblema, el
        /// id, el nombre y el nivel. En la captura de crear «Jondo»: f1{f3 emblema}, f2 42043,
        /// f3 «Jondo», f4 1. En el actor va como opción f5 { f4: este bloque }, y es lo que pone
        /// el nombre del gremio bajo el del personaje.
        /// </summary>
        public static Pb GuildBlock(GuildStore.Guild guild)
            => Pb.New()
                .Msg(1, Pb.New().Msg(3, Emblem(guild)))
                .Var(2, guild.Id)
                .Str(3, guild.Name)
                .Var(4, guild.Level);

        /// <summary>El emblema, los cuatro números del jjg de creación en sus campos 1, 2, 3 y 5.</summary>
        private static Pb Emblem(GuildStore.Guild guild)
            => Pb.New()
                .Var(1, guild.EmblemSymbol)
                .Var(2, guild.EmblemSymbolColor)
                .Var(3, guild.EmblemBackground)
                .Var(5, guild.EmblemSymbolRgb);

        /// <summary>
        /// «Perteneces a este gremio» (jgw): f2 el puesto del personaje, f3 el bloque del gremio.
        /// Byte a byte contra la creación de «Jondo».
        /// </summary>
        public static byte[] BuildGuildJoined(GuildStore.Guild guild, int rank)
            => Pb.New()
                .Var(2, rank)
                .Msg(3, GuildBlock(guild))
                .Build();

        /// <summary>
        /// La cabecera de la ventana de gremio (jhh): f1 la fecha de fundación, f3 el nivel, f9
        /// el máximo de miembros y f10 cuántos hay. Un gremio de nivel 1 recién creado manda
        /// exactamente esos cuatro campos -sin las barras de experiencia f5/f6/f7 que sólo salen
        /// en gremios con experiencia-, y así se reproduce.
        /// </summary>
        public static byte[] BuildGuildInfo(GuildStore.Guild guild, int memberCount)
            => Pb.New()
                .Str(1, guild.FoundedUtc)
                .Var(3, guild.Level)
                .Var(9, GuildStore.MaxMembers(guild.Level))
                .Var(10, memberCount)
                .Build();

        /// <summary>
        /// Los cuatro rangos por defecto de un gremio recién creado (jco). Son un molde fijo,
        /// medido una vez en la creación de «Jondo»: los nombres son claves de traducción
        /// (guild.rank.N.name), y los permisos de cada rango van como una ristra de bytes que
        /// este emulador todavía no interpreta -se copian tal cual, que es lo que hace un gremio
        /// nuevo-. El f4{f2} es el icono del rango y el f5 su número.
        /// </summary>
        public static byte[] BuildDefaultRanks()
        {
            byte[] rights1 = { 0x01, 0x02, 0x05, 0x06, 0x07, 0x08, 0x0d, 0x0e, 0x0f, 0x17, 0x18, 0x19,
                               0x1a, 0x1d, 0x1e, 0x1f, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27,
                               0x28, 0x29, 0x2a, 0x2b };
            byte[] rights2 = { 0x01, 0x02, 0x05, 0x06, 0x26, 0x07, 0x27, 0x08, 0x28, 0x29, 0x0d, 0x0e,
                               0x0f, 0x17, 0x18, 0x19, 0x1a };

            var jco = Pb.New()
                .Msg(2, Pb.New()
                    .Str(2, "guild.rank.1.name")
                    .Msg(3, Pb.New().Bytes(3, rights1))
                    .Msg(4, Pb.New().Var(2, 116))
                    .Var(5, 1))
                .Msg(2, Pb.New()
                    .Str(2, "guild.rank.2.name")
                    .Msg(3, Pb.New().Var(1, 1).Bytes(3, rights2))
                    .Msg(4, Pb.New().Var(2, 115).Var(3, 1))
                    .Var(5, 2))
                .Msg(2, Pb.New()
                    .Str(2, "guild.rank.3.name")
                    .Msg(3, Pb.New().Var(1, 1))
                    .Msg(4, Pb.New().Var(2, 114).Var(3, 2))
                    .Var(5, 3))
                .Msg(2, Pb.New()
                    .Str(2, "guild.rank.4.name")
                    .EmptyMsg(3)
                    .Msg(4, Pb.New().Var(2, 117).Var(3, 3))
                    .Var(5, 4));
            return jco.Build();
        }

        /// <summary>
        /// El gremio visto desde un actor del mapa (jhe): el bloque del gremio, el puesto, la
        /// experiencia del miembro y un f4 constante (1791 en todas las capturas de «Jondo», sin
        /// significado reconstruido). Es lo que el cliente pinta al pasar el ratón por encima.
        /// </summary>
        public const int ActorGuildTrailer = 1791;

        public static byte[] BuildActorGuild(GuildStore.Guild guild, int rank, long memberExperience)
            => Pb.New()
                .Msg(1, GuildBlock(guild))
                .Var(2, rank)
                .VarIfNotZero(3, memberExperience)
                .Var(4, ActorGuildTrailer)
                .Build();

        /// <summary>
        /// Un miembro para la lista de la ventana (jgu). Los campos claros -nombre, nivel, puesto,
        /// cuándo entró, la cuenta, si está conectado- salen del registro; el f5.f3, el f5.f6 y el
        /// f5.f7 se copian del fundador de «Jondo» sin saber qué son, y por eso esta trama no lleva
        /// test de bytes: en cuanto se midan, se calculan.
        /// </summary>
        public static byte[] BuildMember(GuildStore.Member member, string name, int level, long accountId)
            => Pb.New()
                .Msg(1, Pb.New()
                    .Msg(1, Pb.New()
                        .Str(2, name)
                        .Var(3, level)
                        .Msg(5, Pb.New()
                            .Var(2, member.Rank)
                            .Var(3, 1)                                   // measured constant, meaning unknown
                            .Var(4, member.JoinedUtcMs)
                            .Var(6, 11)                                  // measured on the founder; not understood
                            .Msg(7, Pb.New().Var(1, 8094).Str(2, "").Var(3, 3).Str(8, ""))  // idem
                            .Msg(8, Pb.New().Var(1, 1))                  // connected
                            .Var(10, accountId)))
                    .Var(2, member.CharacterId))
                .Build();

        /// <summary>Quita un miembro de la lista (khj): f1 su número de puesto en la lista.</summary>
        public static byte[] BuildMemberGone(int slot)
            => Pb.New().Var(1, slot).Build();

        // ─── La tienda del gremio ───────────────────────────────────────────────

        /// <summary>
        /// La tienda (jkh): f2 { f1 las cuentas activas, y un f2 por artículo con su número y su
        /// precio ya multiplicado }. El f2{f2{f3}} vacío de cada artículo va tal cual, que es lo
        /// que manda el servidor real en los dos gremios medidos -uno de una cuenta y otro de
        /// cuatro-.
        /// </summary>
        public static byte[] BuildShop(int accounts)
        {
            var jkh = Pb.New();
            var cuerpo = Pb.New().Var(1, accounts);
            foreach (var oracle in GuildOracles.All)
            {
                cuerpo.Msg(2, Pb.New()
                    .Var(1, oracle.Id)
                    .Msg(2, Pb.New()
                        .Var(1, GuildOracles.PriceFor(oracle.Id, accounts))
                        .Msg(2, Pb.New().Str(3, ""))));
            }
            return jkh.Msg(2, cuerpo).Build();
        }

        /// <summary>Comprado (jkj con el artículo). El rechazo es el mismo mensaje VACÍO.</summary>
        public static byte[] BuildShopBought(int oracle) => Pb.New().Var(1, oracle).Build();
        public static byte[] BuildShopRefused() => System.Array.Empty<byte>();

        /// <summary>Los kamas de gremio que quedan (jia).</summary>
        public static byte[] BuildGuildKamas(long kamas) => Pb.New().Var(1, kamas).Build();

        /// <summary>
        /// Lo comprado que falta por activar (jkv): f1 { f2 { f1 …, f3 el plazo } }, f2 el
        /// artículo. El f1 de dentro valía 5 en la única compra medida y no se sabe qué es -no
        /// es el artículo, que era el 1, ni las cuentas, que eran cuatro-, así que va como
        /// estaba.
        /// </summary>
        public const int PendingOracleUnknown = 5;

        public static byte[] BuildPendingOracle(int oracle, string deadlineUtc)
            => Pb.New()
                .Msg(1, Pb.New().Msg(2, Pb.New().Var(1, PendingOracleUnknown).Str(3, deadlineUtc)))
                .Var(2, oracle)
                .Build();

        /// <summary>Activado (jkx): f1 el artículo.</summary>
        public static byte[] BuildOracleActivated(int oracle) => Pb.New().Var(1, oracle).Build();

        /// <summary>
        /// Una alteración puesta (lzs): f1 { f1 desde, f2 cuál, f4 2, f5 hasta } en milisegundos.
        /// </summary>
        /// <remarks>
        /// La del «Oráculo de saber» de la captura lleva además dos bloques f3 con el detalle de
        /// lo que hace -efectos 2833 y 2867-. No se reproducen: de los cinco oráculos sólo está
        /// medido ese, y copiarle los efectos a los otros cuatro sería inventárselos. El cliente
        /// pinta el icono y el tiempo con lo que va aquí.
        /// </remarks>
        public static byte[] BuildAlteration(int alteration, long fromMs, long toMs)
            => Pb.New()
                .Msg(1, Pb.New()
                    .Var(1, fromMs)
                    .Var(2, alteration)
                    .Var(4, 2)
                    .Var(5, toMs))
                .Build();

        // ─── Contribuir ─────────────────────────────────────────────────────────

        /// <summary>La contribución hecha (jle): f1 los kamas que se dieron, f2 las que quedan.</summary>
        public static byte[] BuildContribution(long kamas, int left)
            => Pb.New().Var(1, kamas).Var(2, left).Build();

        // ─── Candidaturas e invitaciones ────────────────────────────────────────

        /// <summary>
        /// El bloque de una candidatura, el mismo dentro del jmf y del jly: cuándo se mandó, con
        /// qué texto, y quién la manda -su cuenta, su nombre, su etiqueta, su personaje, el
        /// apodo de la cuenta y su nivel-.
        /// </summary>
        private static Pb ApplicationBlock(GuildStore.Application application, string name, int level,
                                           long accountId, string accountNick, string tag)
            => Pb.New()
                .Var(1, application.WhenMs)
                .Str(3, application.Message)
                .Msg(4, Pb.New()
                    .Var(1, 10)                       // measured on the one application; not understood
                    .Var(3, 1)                        // idem
                    .Var(4, accountId)
                    .Str(5, name)
                    .Str(6, tag)
                    .Var(7, application.CharacterId)
                    .Str(8, accountNick)
                    .Msg(9, Pb.New().Var(1, 1))
                    .Var(10, level));

        /// <summary>
        /// La pestaña de candidaturas (jmf): f2 la pestaña que se pidió, un f3 por candidatura y
        /// f4 cuántas hay. Sin ninguna va sólo la pestaña, que es lo que llega en los gremios sin
        /// candidaturas de las capturas.
        /// </summary>
        public static byte[] BuildApplications(int tab, IReadOnlyList<(GuildStore.Application Application,
                                                                       string Name, int Level, long AccountId,
                                                                       string Nick, string Tag)> applications)
        {
            var jmf = Pb.New().Var(2, tab);
            foreach (var (application, name, level, accountId, nick, tag) in applications)
            {
                jmf.Msg(3, ApplicationBlock(application, name, level, accountId, nick, tag));
            }
            if (applications.Count > 0) jmf.Var(4, applications.Count);
            return jmf.Build();
        }

        /// <summary>Una candidatura suelta (jly): f1 1, f2 la candidatura, f3 a quién se le enseña.</summary>
        public static byte[] BuildApplication(GuildStore.Application application, string name, int level,
                                              long accountId, string nick, string tag, long toCharacter)
            => Pb.New()
                .Var(1, 1)
                .Msg(2, ApplicationBlock(application, name, level, accountId, nick, tag))
                .Var(3, toCharacter)
                .Build();

        /// <summary>«Alguien ha echado una candidatura» (jma): f1 su personaje, f2 su nombre.</summary>
        public static byte[] BuildApplicationArrived(long characterId, string name)
            => Pb.New().Var(1, characterId).Str(2, name).Build();

        /// <summary>
        /// Cómo va una candidatura o una invitación (jin): f1 el nombre, f2 un número que no se
        /// ha reconstruido, f3 el estado. Medido: 1 al aceptarla y 3 cuando el otro entra, con el
        /// sello de entrada del miembro cayendo justo en el segundo jin.
        /// </summary>
        public const int StatusAccepted = 1;
        public const int StatusJoined = 3;

        public static byte[] BuildApplicationStatus(string name, long number, int status)
            => Pb.New().Str(1, name).Var(2, number).Var(3, status).Build();

        /// <summary>
        /// «Te invitan a un gremio» (jiq): f1 el bloque del gremio -el mismo del jgw- y f2 quién
        /// invita. Medido byte a byte en la captura de recibir una invitación.
        /// </summary>
        public static byte[] BuildInvitation(GuildStore.Guild guild, string inviterName)
            => Pb.New()
                .Msg(1, GuildBlock(guild))
                .Str(2, inviterName)
                .Build();

        /// <summary>Lo que cierra la entrada al gremio (jij). Valía 3 al aceptar la invitación.</summary>
        public static byte[] BuildJoinDone() => Pb.New().Var(1, StatusJoined).Build();
    }
}
