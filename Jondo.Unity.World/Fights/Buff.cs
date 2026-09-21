using System;
using System.Collections.Generic;
using System.Linq;

namespace Jondo.Unity.World.Fights
{
    /// <summary>
    /// A qué afecta un embrujo que no toca una característica suelta.
    ///
    /// Los tres salen del catálogo de efectos del cliente y comparten forma: el hechizo afectado
    /// viaja en el <c>diceNum</c> y lo que se le suma, en el <c>value</c>.
    ///
    ///   293  "#1: +#3 de daños básicos"     280  "+#3 de alcance mínimo"
    ///   281  "#1: +#3 de alcance máximo"
    /// </summary>
    public enum SpellAspect
    {
        Nada = 0,
        DanoBase = 293,
        AlcanceMinimo = 280,
        AlcanceMaximo = 281,
    }

    /// <summary>
    /// Un embrujo: lo que un hechizo deja puesto sobre alguien, y hasta cuándo.
    ///
    /// No hay ninguna lista de hechizos escrita a mano detrás de esto. Cada embrujo sale de una
    /// entrada del <c>EffectsJson</c> del hechizo, y lo que significa el número de efecto lo dice
    /// la tabla <c>Effects</c> del cliente. Aquí sólo se guarda lo ya resuelto.
    /// </summary>
    public sealed class Buff
    {
        /// <summary>El número correlativo con el que viaja al cliente, empezando por uno.</summary>
        public int Numero { get; set; }

        public int EffectId { get; set; }
        public int EffectUid { get; set; }

        /// <summary>La característica que toca, o cero si lo suyo va por otro lado.</summary>
        public int Caracteristica { get; set; }

        /// <summary>Cuánto, ya con su signo.</summary>
        public int Cuanto { get; set; }

        /// <summary>Si afecta a un hechizo concreto, cuál y de qué manera.</summary>
        public SpellAspect Sobre { get; set; }
        public int HechizoAfectado { get; set; }

        /// <summary>El estado que pone o quita, si es de los que hacen eso.</summary>
        public int Estado { get; set; }

        /// <summary>La apariencia temporal impuesta por el efecto 335, o cero.</summary>
        public int Apariencia { get; set; }

        public int HechizoOrigen { get; set; }
        public int NivelOrigen { get; set; }
        public long Quien { get; set; }
        public string Disparador { get; set; } = "I";

        /// <summary>La ronda en la que se cae. Menos uno es "hasta que acabe el combate".</summary>
        public int CaducaEnRonda { get; set; }

        /// <summary>
        /// La ronda en la que EMPIEZA a valer. Para casi todos es la del lanzamiento; para los
        /// retardados, tantas rondas después como diga el retardo del efecto.
        ///
        /// Sin esto no había manera de expresar «esto empieza dentro de dos turnos», que es lo que
        /// hace la Flecha Castigadora, y el embrujo se aplicaba entero en el acto.
        /// </summary>
        public int EmpiezaEnRonda { get; set; }

        /// <summary>
        /// Si se suma al que ya hubiera igual en vez de sustituirlo. Lo llevan los que un hechizo
        /// va poniendo cada vez que pasa algo —cada paso, cada golpe recibido—, que se acumulan.
        /// </summary>
        public bool Apila { get; set; }

        /// <summary>
        /// How many equivalent rows may coexist on the bearer: the spell level's
        /// <c>maxStack</c>. Minus one is no limit, zero and one are "the new one replaces the
        /// old", and a larger number is the cap, the oldest giving way.
        /// </summary>
        /// <remarks>
        /// Measured on the Yopuka captures: Fervor (-1) cast twice in a turn keeps both
        /// shields, rows 10 and 11; Pugilato (-1) its two "+18 de daños básicos"; Tumulto (-1)
        /// puts one "+20" per enemy hit, rows 103 and 104 in one cast. Espada del Juicio (1)
        /// cast again drops its shield -- jya 9, jwe 514 -- and puts row 13; Sentencia (1) the
        /// same with its state. Presión and Espada Destructora are 2, which is what "erosiona"
        /// twice on one target adds up to.
        /// </remarks>
        public int MaxStacks { get; set; }

        /// <summary>
        /// An instantaneous heal waiting for <see cref="EmpiezaEnRonda"/>. The amount is resolved
        /// when the spell is cast, like every other delayed buff, but missing-life capping is done
        /// only when the heal becomes due.
        /// </summary>
        public int PendingHealPoints { get; set; }

        /// <summary>
        /// A row that is only WAITING: the effect has a delay and nothing of it applies until
        /// <see cref="EmpiezaEnRonda"/>. The real server registers it with trigger "Y", hidden
        /// from the panel, and drops it when the effect goes off: the kill of a beacon two
        /// rounds after its birth, the script marker and the +1 MP of Paso de Cacería on the
        /// next turn. What it will do is in <see cref="EffectId"/>, <see cref="Caracteristica"/>
        /// and <see cref="Cuanto"/>, and how long it will then last in <see cref="Duracion"/>.
        /// </summary>
        public bool Pendiente { get; set; }

        /// <summary>
        /// The catalogue numbers of the row, kept for the frames: the dice, the value, and how
        /// dispellable it is. A pending row announces them twice, once waiting and once live.
        /// </summary>
        public int Dado { get; set; }
        public int Cara { get; set; }
        public int Valor { get; set; }
        public int Dispellable { get; set; }

        /// <summary>How many rounds a pending row lasts once it goes off.</summary>
        public int Duracion { get; set; }

        /// <summary>
        /// Whether the row is a critical list's, for the f9 of its frames: a waiting row keeps
        /// it and hands it to the live row it turns into (Espada del Destino's "+40" waits
        /// flagged and goes off flagged, row 29 off row 28 in its capture).
        /// </summary>
        public bool Critico { get; set; }

        /// <summary>
        /// The pending row this one came out of, for the client to link the two: the activated
        /// +1 MP of Paso de Cacería carries the number of its "Y" row. Zero when it has none.
        /// </summary>
        public int Padre { get; set; }

        /// <summary>
        /// Whether the row counts right now: started, and still on the bearer. Expiry is the
        /// sweep's business, not this one's -- a row whose round has come stays in force until
        /// the sweep takes it at its caster's turn, which is when the real server drops it.
        /// </summary>
        public bool Vivo(int ronda) => ronda >= EmpiezaEnRonda && !Pendiente;
    }

    /// <summary>
    /// Los embrujos y los estados que uno lleva encima, y las actitudes que le dan sus objetos.
    ///
    /// Va aparte del <see cref="Fighter"/> para que se pueda mirar de un vistazo qué hay puesto y
    /// quién lo puso, que es justo lo que el cliente pinta en el panel de embrujos.
    /// </summary>
    public sealed class Buffs
    {
        private readonly List<Buff> _puestos = new List<Buff>();
        private readonly HashSet<int> _estados = new HashSet<int>();

        /// <summary>
        /// Los hechizos que los objetos regalan: las actitudes de los dofus y de los trofeos, que
        /// vienen del efecto 1175 de cada objeto. Se registran al empezar el combate y son las que
        /// se disparan al principio y al final de cada turno.
        /// </summary>
        public List<int> Actitudes { get; } = new List<int>();

        /// <summary>
        /// The grade an attitude is held at, for the few that have one: the initial spells of a
        /// character's own choices are cast at his grade of the choice -- 25200 "Explobomba" at
        /// grade 3 for the Tymador who has Explobomba at 3 -- while an item's attitude and a
        /// class passive are always their grade one.
        /// </summary>
        private readonly Dictionary<int, int> _gradosDeActitud = new Dictionary<int, int>();

        public void PonerActitud(int hechizo, int grado = 1)
        {
            if (!Actitudes.Contains(hechizo)) Actitudes.Add(hechizo);
            if (grado > 1) _gradosDeActitud[hechizo] = grado;
        }

        public int GradoDeActitud(int hechizo)
            => _gradosDeActitud.TryGetValue(hechizo, out int grado) ? grado : 1;

        /// <summary>
        /// Los hechizos que uno lleva puestos y que TODAVÍA TIENEN ALGO QUE HACER más adelante.
        ///
        /// Un hechizo no se acaba al lanzarlo: sus efectos con disparador distinto de "I" quedan a
        /// la espera de que pase lo suyo. El Centinela del Ocra es el caso claro: al lanzarlo da
        /// diez de alcance y un veinte por ciento de daños a distancia, y luego, POR CADA PASO que
        /// se anda, se come uno de alcance y un dos por ciento. Ese "por cada paso" es el
        /// disparador CCMPARR, y para poder dispararlo hay que acordarse de que el hechizo sigue
        /// puesto.
        ///
        /// Es lo mismo que las actitudes de los objetos, pero con fecha de caducidad.
        /// </summary>
        public List<ActiveSpell> ActiveSpells { get; } = new List<ActiveSpell>();

        /// <summary>Un hechizo que sigue puesto y en qué grado, hasta que se caiga.</summary>
        public sealed class ActiveSpell
        {
            public int Hechizo { get; set; }
            public int Grado { get; set; }
            public int CaducaEnRonda { get; set; }

            /// <summary>
            /// Who cast it, and therefore who its later effects come from: Polvo's "explode if
            /// destroyed" is the Tymador's doing on his bomb, not the bomb's on itself. Zero
            /// means the bearer.
            /// </summary>
            public long Lanzador { get; set; }

            /// <summary>
            /// The round the hook was put in. A hooked row with a delay does not go off before
            /// this round plus its delay: Furor's decay is a "1160 under TE" with a delay of
            /// one, and it fires at the end of a turn of the round AFTER the cast.
            /// </summary>
            public int PuestoEnRonda { get; set; }

            /// <summary>
            /// Whether the cast that put it was critical: the hook fires with the critical
            /// lists. Pugilato's turn-end 406 goes out flagged, from the critical list's own
            /// entry (uid 371557), when the cast was.
            /// </summary>
            public bool Critico { get; set; }

            public bool Vivo(int ronda) => CaducaEnRonda < 0 || ronda < CaducaEnRonda;
        }

        /// <summary>Deja apuntado que este hechizo sigue puesto, o alarga el que ya estaba.</summary>
        public void Enganchar(int hechizo, int grado, int caducaEnRonda, long lanzador = 0, int puestoEnRonda = 0,
                              bool critico = false)
        {
            var ya = _enganchesPorHechizo(hechizo);
            if (ya != null)
            {
                ya.Grado = grado;
                ya.CaducaEnRonda = caducaEnRonda;
                ya.Lanzador = lanzador;
                ya.PuestoEnRonda = puestoEnRonda;
                ya.Critico = critico;
                return;
            }
            ActiveSpells.Add(new ActiveSpell
            {
                Hechizo = hechizo, Grado = grado, CaducaEnRonda = caducaEnRonda, Lanzador = lanzador,
                PuestoEnRonda = puestoEnRonda, Critico = critico,
            });
        }

        private ActiveSpell _enganchesPorHechizo(int hechizo)
            => ActiveSpells.FirstOrDefault(e => e.Hechizo == hechizo);

        /// <summary>Quita los enganches cumplidos.</summary>
        public void BarrerEnganches(int ronda) => ActiveSpells.RemoveAll(e => !e.Vivo(ronda));

        public IReadOnlyList<Buff> Puestos => _puestos;
        public IReadOnlyCollection<int> Estados => _estados;

        public bool TieneEstado(int estado) => _estados.Contains(estado);

        public void PonerEstado(int estado) { if (estado != 0) _estados.Add(estado); }
        public void QuitarEstado(int estado) => _estados.Remove(estado);

        /// <summary>
        /// Quita un estado y los embrujos que lo representaban en el panel. Devolverlos permite
        /// que la capa de red mande un <c>jya</c> por cada uno.
        /// </summary>
        /// <summary>
        /// Le recorta rondas a todos los embrujos y devuelve cuántos se han caído del todo.
        /// </summary>
        /// <remarks>
        /// Es el efecto 1075, «Duración de los efectos: -N», que llevan 167 hechizos —el Grito
        /// Terrorífico del Ouginak con cuatro rondas, por ejemplo—.
        ///
        /// Los que NO caducan por ronda se quedan como están: un embrujo permanente no se acorta,
        /// porque no tiene nada que acortar, y restarle rondas a un cero lo dejaría caducado en la
        /// ronda pasada y se caería entero.
        /// </remarks>
        public int Acortar(int rondas, int ahora)
        {
            if (rondas <= 0) return 0;

            var caidos = new List<Buff>();
            foreach (var embrujo in _puestos)
            {
                if (embrujo.CaducaEnRonda <= 0) continue;

                embrujo.CaducaEnRonda -= rondas;
                if (embrujo.CaducaEnRonda <= ahora) caidos.Add(embrujo);
            }

            foreach (var muerto in caidos) _puestos.Remove(muerto);
            return caidos.Count;
        }

        public List<Buff> QuitarEstadoConEmbrujos(int estado)
        {
            _estados.Remove(estado);
            var quitados = _puestos.FindAll(e => e.Estado == estado);
            _puestos.RemoveAll(e => e.Estado == estado);
            return quitados;
        }

        /// <summary>Retira todo lo que dejó un hechizo, incluidos sus estados y sus enganches.</summary>
        /// <remarks>
        /// The hooks go with the rows: Furor's 406 on 28604 takes the hooked "1160 under TE"
        /// away with the state and the +N -- jya 36, 37 AND 38 in the capture -- and a hook
        /// left behind would fire the decay on the rows the recast has just put.
        /// </remarks>
        public List<Buff> QuitarDelHechizo(int hechizo)
        {
            var quitados = _puestos.FindAll(e => e.HechizoOrigen == hechizo);
            _puestos.RemoveAll(e => e.HechizoOrigen == hechizo);
            ActiveSpells.RemoveAll(e => e.Hechizo == hechizo);

            foreach (var quitado in quitados)
            {
                if (quitado.Estado == 0) continue;
                if (!_puestos.Any(e => e.Estado == quitado.Estado))
                    _estados.Remove(quitado.Estado);
            }
            return quitados;
        }

        /// <summary>
        /// The rows the last <see cref="Poner"/> took off to make room for the new one: the
        /// refreshed row of a spell that does not stack, the oldest of one that stacks up to a
        /// cap. The real server announces each as gone -- jya and jwe 514 -- before the new
        /// row; the caller reads them right after the put.
        /// </summary>
        public List<Buff> Relevados { get; } = new List<Buff>();

        /// <summary>
        /// Añade un embrujo con el número que le toque. El número NO es de cada luchador: es
        /// correlativo del combate entero, y el que se usa luego para quitarlo con el jya. En la
        /// captura los del jugador van del 19 al 25 y los del monstruo siguen del 26 al 32, misma
        /// serie.
        /// </summary>
        /// <remarks>
        /// How many equivalent rows may live together is the spell level's <c>maxStack</c>, in
        /// <see cref="Buff.MaxStacks"/>: the new row replaces the old at zero and one, joins it
        /// without limit at minus one, and pushes the oldest out at a cap. Two rows are
        /// equivalent when they are the same catalogue entry of the same spell by the same
        /// caster -- the entry, not just the effect: Flecha Castigadora carries two 293 that
        /// only differ by their effectUid, and both stay. A replaced row is never refreshed in
        /// place: the real server never reuses a number (in "tymador-explobomba resiliente"
        /// the same bomb carries state 2484 as buff 19 and again as buff 23), it takes the old
        /// one off and puts a new one, and the ones taken off are left in
        /// <see cref="Relevados"/> for the caller to announce.
        ///
        /// <see cref="Buff.Apila"/> is the old "stack without limit" of the rows a spell puts
        /// each time something happens -- a combo rung, a hooked "-N de daños recibidos", a
        /// per-step malus -- and stays so unless the level writes a cap above one.
        /// </remarks>
        public Buff Poner(Buff embrujo, Func<int> siguienteNumero)
        {
            Relevados.Clear();

            int tope = embrujo.MaxStacks;
            if (embrujo.Apila && tope <= 1) tope = -1;

            if (tope >= 0)
            {
                int cabe = Math.Max(1, tope);
                var equivalentes = _puestos.FindAll(e => e.EffectId == embrujo.EffectId
                                                      && e.EffectUid == embrujo.EffectUid
                                                      && e.HechizoOrigen == embrujo.HechizoOrigen
                                                      && e.HechizoAfectado == embrujo.HechizoAfectado
                                                      && e.Quien == embrujo.Quien
                                                      && e.Pendiente == embrujo.Pendiente);
                while (equivalentes.Count >= cabe)
                {
                    var relevado = equivalentes[0];
                    equivalentes.RemoveAt(0);
                    _puestos.Remove(relevado);
                    Relevados.Add(relevado);
                }
            }

            embrujo.Numero = siguienteNumero();
            _puestos.Add(embrujo);
            return embrujo;
        }

        /// <summary>Lo que suman los embrujos a una característica.</summary>
        public int De(int caracteristica, int ronda)
        {
            int total = 0;
            foreach (var e in _puestos)
            {
                if (e.Caracteristica == caracteristica && e.Vivo(ronda)) total += e.Cuanto;
            }
            return total;
        }

        /// <summary>
        /// Lo que MULTIPLICAN los embrujos de un número de efecto, en tanto por ciento.
        ///
        /// Hay una familia que no suma sino que multiplica: el 1163 es "daños sufridos x#1%" y el
        /// 1159 "curas recibidas x#1%". No tienen característica en el catálogo —el cliente los
        /// resuelve por su número—, así que no valen los mismos caminos que el resto.
        ///
        /// Devuelve cien cuando no hay ninguno, o sea "por uno". Varios se encadenan: dos del
        /// ciento diez dan un ciento veintiuno.
        /// </summary>
        /// <summary>
        /// The flat "-N de daños recibidos" (105, 265) the bearer holds against a blow of the
        /// given kinds: the rows put with no trigger at all, and the rows whose trigger names
        /// one of the kinds. Those letters are not triggers that fire but CONDITIONS on the
        /// blow -- "DR" ranged, "DM"/"DCAC" melee, "D" any, "DTB"/"DTE" the poisons of a turn
        /// -- and Remisión's on a bomb is "DR": a bomb shot from afar takes 20 less at grade 3,
        /// one hit from next door takes it all.
        /// </summary>
        public int ReduccionDeDanoRecibido(int ronda, IReadOnlyCollection<string> clasesDelGolpe)
        {
            int total = 0;
            foreach (var e in _puestos)
            {
                if (e.EffectId != DanoRecibidoMenos && e.EffectId != DanoRecibidoMenosFijo) continue;
                if (!e.Vivo(ronda) || e.Cuanto <= 0) continue;
                bool aplica = string.IsNullOrEmpty(e.Disparador) || e.Disparador == "I";
                if (!aplica)
                {
                    foreach (var d in e.Disparador.Split('|'))
                    {
                        if (clasesDelGolpe.Contains(d.Trim())) { aplica = true; break; }
                    }
                }
                if (aplica) total += e.Cuanto;
            }
            return total;
        }

        /// <summary>The two "-N de daños recibidos" of the catalogue.</summary>
        public const int DanoRecibidoMenos = 265;
        public const int DanoRecibidoMenosFijo = 105;

        /// <summary>"Daños sufridos x#1%", the multiplier of Represalias, Tiro Penetrante and Salto.</summary>
        public const int DanoSufridoPorCiento = 1163;

        /// <param name="clasesDelGolpe">
        /// The kinds of the blow being read, for the rows registered under a damage kind
        /// (Salto's "1163 under D"): a row with a kind counts only for a blow of that kind.
        /// Without the list every live row counts.
        /// </param>
        public int Multiplicador(int efecto, int ronda, IReadOnlyCollection<string> clasesDelGolpe = null)
        {
            double total = 1.0;
            bool alguno = false;
            foreach (var e in _puestos)
            {
                if (e.EffectId != efecto || !e.Vivo(ronda)) continue;
                if (e.Cuanto == 0) continue;
                if (clasesDelGolpe != null && !string.IsNullOrEmpty(e.Disparador) && e.Disparador != "I")
                {
                    bool aplica = false;
                    foreach (var d in e.Disparador.Split('|'))
                    {
                        if (clasesDelGolpe.Contains(d.Trim())) { aplica = true; break; }
                    }
                    if (!aplica) continue;
                }
                total *= e.Cuanto / 100.0;
                alguno = true;
            }
            return alguno ? (int)Math.Round(total * 100) : 100;
        }

        /// <summary>Lo que suman los embrujos a un hechizo concreto: daño base o alcance.</summary>
        public int DelHechizo(int hechizo, SpellAspect que, int ronda)
        {
            int total = 0;
            foreach (var e in _puestos)
            {
                if (e.Sobre == que && e.HechizoAfectado == hechizo && e.Vivo(ronda)) total += e.Cuanto;
            }
            return total;
        }

        /// <summary>La última apariencia temporal que siga activa, o cero.</summary>
        public int AparienciaEn(int ronda)
        {
            for (int i = _puestos.Count - 1; i >= 0; i--)
            {
                var e = _puestos[i];
                if (e.Apariencia != 0 && e.Vivo(ronda)) return e.Apariencia;
            }
            return 0;
        }

        /// <summary>Se lleva los que ya han caducado y devuelve cuáles eran.</summary>
        /// <param name="leTocaCaer">
        /// Which of the expired rows fall NOW. Without it, all of them. The turn start passes
        /// the rule the captures show: a row falls at the start of its caster's turn, not at
        /// the first turn of its round. Measured over the class captures on the rounds a
        /// monster opens -- the only ones that tell the two apart -- thirteen rows put by the
        /// player fall at his own turn and one at the monster's; and fifty-seven rows put by a
        /// summon fall at the summon's own turn.
        /// </param>
        public List<Buff> Barrer(int ronda, Func<Buff, bool> leTocaCaer = null)
        {
            // Se barre lo que ha CADUCADO, no lo que «no esta vivo».
            //
            // No es lo mismo desde que existen los embrujos retardados: uno que todavia no ha
            // empezado tampoco esta vivo, y con la condicion de antes lo barria la primera vez que
            // pasaba la escoba, o sea el turno siguiente a lanzarlo.
            //
            // Eso es justo lo que se veia con la Flecha Castigadora: sus dos bonos aparecian con
            // su cuenta atras -el 3 y el 2- y al turno siguiente desaparecian dejando la cadena
            // vacia, sin llegar a aplicarse nunca. Nacian y se los llevaba la escoba antes de que
            // les tocara empezar.
            bool cae(Buff e) => Caducado(e, ronda) && (leTocaCaer == null || leTocaCaer(e));
            var caidos = _puestos.FindAll(cae);
            _puestos.RemoveAll(cae);

            // Un estado temporal no puede sobrevivir al embrujo que lo puso. Se conserva si
            // todavía queda otro embrujo vivo que represente el mismo estado.
            foreach (var caido in caidos)
            {
                if (caido.Estado == 0) continue;
                if (!_puestos.Any(e => e.Estado == caido.Estado)) _estados.Remove(caido.Estado);
            }
            return caidos;
        }

        /// <summary>
        /// Removes and returns delayed one-shot heals whose start round has arrived. They must be
        /// taken before the regular expiry sweep because a zero-duration effect expires one round
        /// after it starts.
        /// </summary>
        public List<Buff> TakeDueHealing(int round)
        {
            var due = _puestos.FindAll(e => e.PendingHealPoints > 0 && round >= e.EmpiezaEnRonda);
            _puestos.RemoveAll(e => e.PendingHealPoints > 0 && round >= e.EmpiezaEnRonda);
            return due;
        }

        /// <summary>
        /// Removes and returns the pending rows whose round has come, in the order they were
        /// put. Like the delayed heals, before the expiry sweep: a pending row's expiry is its
        /// activation round, and the sweep would take it as merely expired.
        /// </summary>
        public List<Buff> TakeDuePending(int round)
        {
            var due = _puestos.FindAll(e => e.Pendiente && round >= e.EmpiezaEnRonda);
            _puestos.RemoveAll(e => e.Pendiente && round >= e.EmpiezaEnRonda);
            return due;
        }

        /// <summary>Si a un embrujo se le ha pasado la hora. Uno que aun no ha empezado, NO.</summary>
        /// <remarks>A pending row never expires on its own: it goes when it goes off.</remarks>
        private static bool Caducado(Buff embrujo, int ronda)
            => !embrujo.Pendiente && embrujo.CaducaEnRonda >= 0 && ronda >= embrujo.CaducaEnRonda;

        public void Vaciar()
        {
            _puestos.Clear();
            _estados.Clear();
            Actitudes.Clear();
            _gradosDeActitud.Clear();
        }
    }
}
