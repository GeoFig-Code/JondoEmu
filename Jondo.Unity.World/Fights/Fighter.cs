using System;
using System.Collections.Generic;

namespace Jondo.Unity.World.Fights
{
    public class Fighter
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public int TeamId { get; set; } // 0 = Team Blue (Players), 1 = Team Red (Monsters)
        public int CellId { get; set; }
        public bool IsMonster { get; set; }
        public int MonsterId { get; set; }
        public int GradeIndex { get; set; } = 0;
        public int Level { get; set; }
        public int LookBoneId { get; set; }
        public string Look { get; set; } = "";

        // Combat Stats
        public int MaxHP { get; set; }
        public int CurrentHP { get; set; }
        public int MaxAP { get; set; }
        public int CurrentAP { get; set; }
        public int MaxMP { get; set; }
        public int CurrentMP { get; set; }
        public int Initiative { get; set; }

        /// <summary>Experience this fighter awards on death (gradeXp from its record).</summary>
        public int XpReward { get; set; }

        // Elemental Stats
        public int Strength { get; set; }
        public int Intelligence { get; set; }
        public int Chance { get; set; }
        public int Agility { get; set; }
        public int Power { get; set; }

        /// <summary>La vitalidad, base más equipo. Va en la ficha; la vida sale de MaxHP.</summary>
        public int Vitality { get; set; }

        /// <summary>Critical points granted by the equipment, added on top of the spell's own.</summary>
        public int CriticalBonus { get; set; }

        /// <summary>
        /// Daños fijos generales (característica 16). Se suman al final del cálculo, después de
        /// multiplicar por la característica elemental y la potencia.
        /// </summary>
        public int FlatDamage { get; set; }

        /// <summary>
        /// Daños críticos (característica 86). Sólo se suman cuando el golpe sale crítico, y van
        /// donde los daños fijos: al final, no multiplicados.
        /// </summary>
        public int CriticalDamage { get; set; }

        /// <summary>
        /// Daños fijos de cada elemento (características 88 a 92: tierra, fuego, agua, aire y
        /// neutral). Sólo cuenta el del elemento con el que se pega.
        /// </summary>
        public int EarthDamage { get; set; }
        public int FireDamage { get; set; }
        public int WaterDamage { get; set; }
        public int AirDamage { get; set; }
        public int NeutralDamage { get; set; }

        /// <summary>
        /// Daños de empuje (característica 84) y alcance (19). Los pide el cliente para dibujar la
        /// previsualización: la del desplazamiento sale del empuje y la de a dónde se puede tirar,
        /// del alcance.
        /// </summary>
        public int PushDamage { get; set; }
        public int Range { get; set; }

        /// <summary>
        /// Todo lo demás de la ficha, por número de característica: huida, placaje, esquivas,
        /// resistencias porcentuales, invocaciones...
        ///
        /// Va en un diccionario y no en un campo por cada una porque son treinta y tantas, no
        /// intervienen en ninguna cuenta del servidor y lo único que hacen falta es mandarlas. El
        /// día que alguna se use para algo —la huida contra el placaje, por ejemplo— se le pone su
        /// campo y se saca de aquí.
        /// </summary>
        public Dictionary<int, int> Otras { get; } = new Dictionary<int, int>();

        public int Otra(int caracteristica)
            => Otras.TryGetValue(caracteristica, out int valor) ? valor : 0;

        /// <summary>Los daños fijos del elemento con el que se está pegando.</summary>
        public int GetFlatDamageForElement(ElementType element)
        {
            return element switch
            {
                ElementType.Earth => EarthDamage,
                ElementType.Fire => FireDamage,
                ElementType.Water => WaterDamage,
                ElementType.Air => AirDamage,
                ElementType.Neutral => NeutralDamage,
                _ => 0
            };
        }

        // Resistances (% and Flat)
        public int NeutralResPct { get; set; }
        public int EarthResPct { get; set; }
        public int FireResPct { get; set; }
        public int WaterResPct { get; set; }
        public int AirResPct { get; set; }

        public bool IsAlive => CurrentHP > 0;
        public bool IsReady { get; set; }

        // Spells available to this fighter
        public List<int> SpellIds { get; set; } = new List<int>();

        /// <summary>Grade of each spell (monsters do not always have it at level 1).</summary>
        public Dictionary<int, int> SpellGrades { get; set; } = new Dictionary<int, int>();

        public int AccumulatedMpLoss { get; set; } = 0;
        public int AccumulatedApLoss { get; set; } = 0;

        // ─── Lo que limita los lanzamientos ─────────────────────────────────────

        /// <summary>
        /// Las rondas que le faltan a cada hechizo para poder volver a lanzarse.
        ///
        /// Una clave que entra aquí NO SE BORRA: baja hasta cero y se queda. Es lo que hace el
        /// servidor real, cuyo jxc sigue nombrando los hechizos con un cero ronda tras ronda.
        /// </summary>
        public Dictionary<int, int> Recarga { get; } = new Dictionary<int, int>();

        /// <summary>Veces que se ha lanzado cada hechizo en ESTE turno.</summary>
        public Dictionary<int, int> LanzadosEsteTurno { get; } = new Dictionary<int, int>();

        /// <summary>Veces que se ha lanzado cada hechizo sobre cada objetivo en ESTE turno.</summary>
        public Dictionary<(int Hechizo, long Objetivo), int> LanzadosPorObjetivo { get; }
            = new Dictionary<(int, long), int>();

        // ─── Los invocados ──────────────────────────────────────────────────────

        /// <summary>
        /// De quién es, si a éste lo ha invocado alguien. Cero cuando no lo es.
        ///
        /// Una baliza del Ocra, un glifo o una trampa NO son embrujos: son combatientes con su
        /// identificador negativo, su casilla, su bando, su ficha y su turno. En las capturas el
        /// servidor los manda con el mismo molde que a un monstruo y luego les toca jugar.
        /// </summary>
        public long Invocador { get; set; }

        public bool EsInvocado => Invocador != 0;

        /// <summary>
        /// El hechizo con el que se porta, el <c>startingSpellId</c> de su grado en la tabla de
        /// bichos. Es lo que le da su comportamiento: sus efectos son enganches 792 —"al empezar
        /// mi turno lanza mi grado 2"— igual que las actitudes que regalan los dofus.
        /// </summary>
        public int HechizoPropio { get; set; }

        /// <summary>
        /// La ronda en la que se deshace solo. Menos uno mientras no tenga cuenta atrás. Lo pone
        /// el efecto 141, que el servidor le cuelga al nacer.
        /// </summary>
        public int MuereEnRonda { get; set; } = -1;

        /// <summary>
        /// Si le toca turno en el carrusel.
        ///
        /// No todos los invocados juegan. Medido en las capturas: la Baliza de Supervivencia
        /// recibe su jzc con reloj 150 justo detrás de su Ocra y lo cierra en el acto con un jyt;
        /// la Baliza Táctica NO recibe ni uno en todo el combate. La diferencia está en su
        /// hechizo: la primera tiene un enganche de principio de turno y la segunda sólo reacciona
        /// a los daños y a los empujes, así que no tiene nada que hacer cuando le tocaría.
        /// </summary>
        public bool JuegaTurno { get; set; } = true;

        /// <summary>
        /// The spells a summon casts, each at its grade, for the jyy of whoever controls it.
        /// Empty for anybody who is not a summon.
        /// </summary>
        public IReadOnlyList<(int Spell, int Grade)> HechizosDeInvocado { get; set; }
            = System.Array.Empty<(int, int)>();

        /// <summary>
        /// Whether <paramref name="characterId"/> plays this fighter: himself, or a summon of
        /// his. Every summon is played by its summoner in this client -- the Osamodas' animals,
        /// the Tymobot, the Ocra's beacon: the real server sends the owner a jyy with the
        /// summon's spells when it appears and a jyj when its turn comes, and the owner's jrw
        /// and jwh then move and cast it.
        /// </summary>
        public bool ControlledBy(long characterId)
            => Id == characterId || (EsInvocado && Invocador == characterId);

        /// <summary>Who carries this fighter (effect 50), or zero. A carried fighter shares the carrier's cell and holds no cell of his own.</summary>
        public long CarriedBy { get; set; }

        /// <summary>Whom this fighter carries, or zero.</summary>
        public long Carrying { get; set; }

        public bool EstaCargado => CarriedBy != 0;

        /// <summary>
        /// A copy left by Tymadura: it holds a cell and can tackle, plays no turn, sits in no
        /// carousel, and goes with the first point of damage, with the whole set when its owner
        /// is hit, and at its owner's next turn in any case.
        /// </summary>
        public bool EsIlusion { get; set; }

        /// <summary>The copies this fighter has out, by id. Empty for everybody else.</summary>
        public List<long> Ilusiones { get; } = new List<long>();

        /// <summary>
        /// How much of its summoner's capacity this fighter takes up, copied from the template's
        /// <c>summonCost</c> when it is summoned. Zero means it is free: the Rogue's bombs, the
        /// Ocra's beacons and 483 other templates cost nothing at all, and a couple of the
        /// Osamodas' big summons cost two or three on their own.
        ///
        /// Meaningless on anyone who was not summoned, and left at one there so a fighter that
        /// somehow reaches the counter without a template still occupies a slot rather than
        /// silently occupying none.
        /// </summary>
        public int SummonCost { get; set; } = 1;

        /// <summary>
        /// Lo que lleva puesto encima: embrujos, estados y las actitudes que le dan sus objetos.
        /// </summary>
        public Buffs Buffs { get; } = new Buffs();

        /// <summary>
        /// Si le han pegado desde su turno anterior. Lo mira el disparador "DBE" de las actitudes,
        /// que es de donde sale la regla del Dofus Ocre.
        /// </summary>
        public bool LeHanPegado { get; set; }

        /// <summary>
        /// Temporary bonus to the base damage of a specific spell, together with the round it
        /// expires on. Frozen Arrow, for instance, leaves +4 base damage for 3 turns, and recasting
        /// it refreshes the deadline instead of stacking again (max stack 1).
        /// </summary>
        public Dictionary<int, (int Bonus, int ExpiresRound)> SpellDamageBuffs { get; }
            = new Dictionary<int, (int, int)>();

        public void ApplySpellDamageBuff(int spellId, int bonus, int duration, int currentRound)
        {
            SpellDamageBuffs[spellId] = (bonus, currentRound + duration);
        }

        public int GetSpellDamageBonus(int spellId, int currentRound)
        {
            if (!SpellDamageBuffs.TryGetValue(spellId, out var b)) return 0;
            if (currentRound >= b.ExpiresRound)
            {
                SpellDamageBuffs.Remove(spellId);
                return 0;
            }
            return b.Bonus;
        }

        public void StartTurn()
        {
            CurrentAP = MaxAP;
            CurrentMP = MaxMP;
            AccumulatedMpLoss = 0;
            AccumulatedApLoss = 0;
        }

        /// <summary>Dónde estaba antes del último movimiento. Menos uno si no se ha movido.</summary>
        /// <remarks>
        /// Lo pide el efecto 1100, «teletransporta a la posición anterior», que deshace el último
        /// desplazamiento. Sin esta memoria el efecto no tiene a dónde devolver a nadie, y mandar
        /// a un sitio cualquiera sería peor que no hacer nada.
        /// </remarks>
        public int CasillaAnterior { get; private set; } = -1;

        /// <summary>Mueve al combatiente y se acuerda de dónde estaba.</summary>
        public void MoverA(int casilla)
        {
            if (casilla == CellId) return;
            CasillaAnterior = CellId;
            CellId = casilla;
        }

        public void TakeDamage(int damage)
        {
            CurrentHP = Math.Max(0, CurrentHP - damage);
        }

        // ─── El escudo ──────────────────────────────────────────────────────────

        /// <summary>
        /// Puntos de escudo: se gastan antes que la vida y no se curan.
        /// </summary>
        /// <remarks>
        /// Los ponen dos familias de efectos, 401 hechizos entre las dos: el 1020, que da un
        /// tanto por ciento del NIVEL del lanzador, y el 1039, un tanto por ciento de la VIDA.
        /// El catálogo del cliente los declara sin característica, así que no salen del camino
        /// genérico de los boosts: hacen falta aquí.
        ///
        /// No es vida: no se cura, no cuenta para la muerte y desaparece cuando caduca. Meterlo
        /// en CurrentHP habría sido más corto y habría dejado a un personaje escudado curándose
        /// hasta el tope del escudo.
        /// </remarks>
        public int PuntosDeEscudo { get; private set; }

        /// <summary>La ronda en la que el escudo se cae. Cero cuando no hay escudo.</summary>
        public int EscudoCaducaEnRonda { get; private set; }

        /// <summary>Añade escudo. Se suma al que hubiera y se queda la caducidad más lejana.</summary>
        public void Escudar(int cuanto, int caducaEnRonda)
        {
            if (cuanto <= 0) return;

            PuntosDeEscudo += cuanto;
            if (caducaEnRonda > EscudoCaducaEnRonda) EscudoCaducaEnRonda = caducaEnRonda;
        }

        /// <summary>
        /// Le mete un golpe al escudo primero y devuelve lo que llega a la vida.
        /// </summary>
        public int PasarPorElEscudo(int dano)
        {
            if (dano <= 0 || PuntosDeEscudo <= 0) return dano;

            int aguanta = Math.Min(PuntosDeEscudo, dano);
            PuntosDeEscudo -= aguanta;
            return dano - aguanta;
        }

        /// <summary>Quita el escudo si ya le tocaba caerse.</summary>
        public void CaducarElEscudo(int ronda)
        {
            if (PuntosDeEscudo <= 0) return;
            if (EscudoCaducaEnRonda == 0 || ronda < EscudoCaducaEnRonda) return;

            PuntosDeEscudo = 0;
            EscudoCaducaEnRonda = 0;
        }

        // ─── La erosión ─────────────────────────────────────────────────────────

        /// <summary>
        /// Los puntos de vida MÁXIMA que se han perdido para siempre en este combate.
        ///
        /// Cada golpe no sólo quita vida: se lleva además un pellizco del tope. Con mil de vida y
        /// un golpe de cien con un quince por ciento de erosión, uno se queda en 900/985: los cien
        /// de daño salen de la vida de ahora y quince del tope.
        ///
        /// Se guarda aparte de <see cref="MaxHP"/> —que ya va bajando— porque hay efectos que
        /// pegan EN FUNCIÓN de lo erosionado: el 1092 de Represalias hace un veinte por ciento de
        /// lo que uno lleve erosionado.
        /// </summary>
        public int VidaErosionada { get; private set; }

        /// <summary>La característica 75 del catálogo: el tanto por ciento que erosiona.</summary>
        public const int CaracteristicaDeErosion = 75;

        /// <summary>
        /// The erosion everybody starts with, as a percentage of every hit taken off the maximum.
        /// </summary>
        /// <remarks>
        /// Ten for players and monsters alike. Measured on the Ocra capture, whose sheet carries a
        /// 75 worth 10, and on the damage blocks themselves: 977 of the 986 in the captures carry
        /// an f5, and a hit of 413 on a monster carries 41. The sheet we send to the client
        /// already said ten; the server side said zero, so nobody ever eroded and no hit ever
        /// carried its f5.
        /// </remarks>
        public const int ErosionBase = 10;

        /// <summary>
        /// Erosiona por un golpe y devuelve cuánto tope se ha perdido.
        ///
        /// <paramref name="porciento"/> es la erosión de quien recibe, que sale de su
        /// característica 75 más lo que le hayan puesto encima.
        /// </summary>
        public int Erosionar(int dano, int porciento)
        {
            if (dano <= 0 || porciento <= 0) return 0;

            int pierde = dano * porciento / 100;
            // No se erosiona por debajo de la mitad del tope de salida, que es donde el juego lo
            // corta. El tope de salida es el de ahora más lo que ya se haya perdido.
            int original = MaxHP + VidaErosionada;
            int cabe = Math.Max(0, (original / 2) - VidaErosionada);
            pierde = Math.Min(pierde, cabe);
            if (pierde <= 0) return 0;

            VidaErosionada += pierde;
            MaxHP -= pierde;
            if (CurrentHP > MaxHP) CurrentHP = MaxHP;
            return pierde;
        }

        public int GetStatForElement(ElementType element)
        {
            return element switch
            {
                ElementType.Earth => Strength,
                ElementType.Fire => Intelligence,
                ElementType.Water => Chance,
                ElementType.Air => Agility,
                ElementType.Neutral => Math.Max(Strength, Math.Max(Intelligence, Math.Max(Chance, Agility))),
                _ => 0
            };
        }

        public int GetResPctForElement(ElementType element)
        {
            return element switch
            {
                ElementType.Neutral => NeutralResPct,
                ElementType.Earth => EarthResPct,
                ElementType.Fire => FireResPct,
                ElementType.Water => WaterResPct,
                ElementType.Air => AirResPct,
                _ => 0
            };
        }
    }
}
