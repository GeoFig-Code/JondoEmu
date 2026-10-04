using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Il2CppCore.DataCenter;
using Il2CppCore.UILogic.Components;
using Il2CppCore.UILogic.Components.Figma;
using MelonLoader;
using UnityEngine.UIElements;

namespace JondoFix
{
    /// <summary>
    /// The administrator's window, past its items tab: a character's level, characteristics and
    /// kamas; teleports; NPCs and monsters put on the map where he stands and taken off it; and
    /// the jail. Hosted by <see cref="AdminItemsUi"/>, whose window, tabs and helpers it shares.
    /// </summary>
    /// <remarks>
    /// Built from the client's own pieces, as its menus are: DofusButtonCustom with the client's
    /// primary and secondary styles for what acts, TextInput for what is typed, Divider between
    /// sections. Everything it does is asked of the server's control API with the account's
    /// token -- /api/personaje, /api/conectados, /api/coordenadas, /api/mapa, /api/invocar,
    /// /api/quitar, /api/carcel, /api/liberar, /api/presos -- and the server checks the
    /// administrator's role on every request; nothing here decides anything.
    /// </remarks>
    internal static class AdminWorldUi
    {
        // ─── Texts ───────────────────────────────────────────────────────────────────────

        private static readonly Dictionary<string, Dictionary<string, string>> Texts =
            new Dictionary<string, Dictionary<string, string>>
            {
                ["who"] = AdminItemsUi.L("Personaje", "Character", "Personnage"),
                ["me"] = AdminItemsUi.L("yo", "me", "moi"),
                ["refresh"] = AdminItemsUi.L("Actualizar", "Refresh", "Actualiser"),
                ["jailed"] = AdminItemsUi.L("preso", "jailed", "en prison"),
                ["working"] = AdminItemsUi.L("Enviando…", "Sending…", "Envoi…"),
                ["done"] = AdminItemsUi.L("Hecho.", "Done.", "Fait."),

                ["char.title"] = AdminItemsUi.L("Nivel, características y kamas", "Level, characteristics and kamas", "Niveau, caractéristiques et kamas"),
                ["char.hint"] = AdminItemsUi.L("Lo que se deje en blanco no cambia.", "What is left blank does not change.", "Ce qui reste vide ne change pas."),
                ["char.level"] = AdminItemsUi.L("Nivel", "Level", "Niveau"),
                ["char.vitality"] = AdminItemsUi.L("Vitalidad", "Vitality", "Vitalité"),
                ["char.wisdom"] = AdminItemsUi.L("Sabiduría", "Wisdom", "Sagesse"),
                ["char.strength"] = AdminItemsUi.L("Fuerza", "Strength", "Force"),
                ["char.intelligence"] = AdminItemsUi.L("Inteligencia", "Intelligence", "Intelligence"),
                ["char.chance"] = AdminItemsUi.L("Suerte", "Chance", "Chance"),
                ["char.agility"] = AdminItemsUi.L("Agilidad", "Agility", "Agilité"),
                ["char.kamas"] = AdminItemsUi.L("Kamas", "Kamas", "Kamas"),
                ["char.apply"] = AdminItemsUi.L("Aplicar", "Apply", "Appliquer"),
                ["char.mount"] = AdminItemsUi.L("Montura (id del objeto)", "Mount (item id)", "Monture (id de l'objet)"),
                ["char.give_mount"] = AdminItemsUi.L("Dar montura", "Give mount", "Donner la monture"),
                ["char.nothing"] = AdminItemsUi.L("No hay nada que cambiar.", "There is nothing to change.", "Il n'y a rien à changer."),

                ["tp.title"] = AdminItemsUi.L("Teletransportar", "Teleport", "Téléporter"),
                ["tp.where"] = AdminItemsUi.L("Mapa (id) o coordenadas x,y", "Map (id) or coordinates x,y", "Carte (id) ou coordonnées x,y"),
                ["tp.cell"] = AdminItemsUi.L("Casilla (opcional)", "Cell (optional)", "Cellule (facultatif)"),
                ["tp.go"] = AdminItemsUi.L("Teletransportar", "Teleport", "Téléporter"),
                ["tp.go_to"] = AdminItemsUi.L("Ir donde está", "Go to them", "Aller jusqu'à lui"),
                ["tp.bring"] = AdminItemsUi.L("Traer aquí", "Bring here", "Amener ici"),
                ["tp.pick_other"] = AdminItemsUi.L("Elige a otro personaje.", "Pick another character.", "Choisissez un autre personnage."),
                ["tp.bad"] = AdminItemsUi.L("Escribe un id de mapa o unas coordenadas como -1,0.", "Type a map id or coordinates such as -1,0.", "Tapez un id de carte ou des coordonnées comme -1,0."),

                ["spawn.npcs"] = AdminItemsUi.L("PNJ", "NPCs", "PNJ"),
                ["spawn.monsters"] = AdminItemsUi.L("Monstruos", "Monsters", "Monstres"),
                ["spawn.search"] = AdminItemsUi.L("Nombre, id o niveles 1-50", "Name, id or levels 1-50", "Nom, id ou niveaux 1-50"),
                ["spawn.pick"] = AdminItemsUi.L("Elige un PNJ o un monstruo de la lista.", "Pick an NPC or a monster from the list.", "Choisissez un PNJ ou un monstre dans la liste."),
                ["spawn.npc"] = AdminItemsUi.L("Invocar PNJ aquí", "Spawn NPC here", "Invoquer le PNJ ici"),
                ["spawn.grade"] = AdminItemsUi.L("Grado {0} · nivel {1}", "Grade {0} · level {1}", "Grade {0} · niveau {1}"),
                ["spawn.add"] = AdminItemsUi.L("Añadir al grupo", "Add to group", "Ajouter au groupe"),
                ["spawn.group"] = AdminItemsUi.L("Grupo ({0}/8)", "Group ({0}/8)", "Groupe ({0}/8)"),
                ["spawn.group_go"] = AdminItemsUi.L("Invocar grupo aquí", "Spawn group here", "Invoquer le groupe ici"),
                ["spawn.clear"] = AdminItemsUi.L("Vaciar", "Clear", "Vider"),
                ["spawn.full"] = AdminItemsUi.L("Un grupo tiene ocho como mucho.", "A group holds eight at most.", "Un groupe compte huit au plus."),
                ["spawn.here"] = AdminItemsUi.L("En este mapa ({0}) · se quita hasta reiniciar el servidor", "On this map ({0}) · removed until the server restarts", "Sur cette carte ({0}) · retiré jusqu'au redémarrage du serveur"),
                ["spawn.remove"] = AdminItemsUi.L("Quitar", "Remove", "Retirer"),
                ["spawn.cell"] = AdminItemsUi.L("casilla {0}", "cell {0}", "cellule {0}"),
                ["spawn.empty"] = AdminItemsUi.L("No hay PNJ ni monstruos.", "There are no NPCs or monsters.", "Il n'y a ni PNJ ni monstres."),
                ["spawn.page"] = AdminItemsUi.L("Página {0} de {1} · {2}", "Page {0} of {1} · {2}", "Page {0} sur {1} · {2}"),

                ["jail.title"] = AdminItemsUi.L("Mandar a la cárcel (10 minutos)", "Send to jail (10 minutes)", "Envoyer en prison (10 minutes)"),
                ["jail.hint"] = AdminItemsUi.L("Va a una celda de la Prisión de los GM; tú, al pasillo de al lado. Dentro no puede teletransportarse, ni usar comandos, ni hablar más que por el canal general y por privado.", "They go to a cell of the GM Prison; you, to the corridor beside it. Inside they cannot teleport, use commands, or speak but on the general channel and in private.", "Il va dans une cellule de la Prison des MJ ; vous, dans le couloir à côté. Dedans, il ne peut ni se téléporter, ni utiliser de commandes, ni parler ailleurs que sur le canal général et en privé."),
                ["jail.send"] = AdminItemsUi.L("Encarcelar", "Jail", "Emprisonner"),
                ["jail.inside"] = AdminItemsUi.L("En la cárcel", "In jail", "En prison"),
                ["jail.release"] = AdminItemsUi.L("Liberar", "Release", "Libérer"),
                ["jail.nobody"] = AdminItemsUi.L("No hay nadie en la cárcel.", "Nobody is in jail.", "Personne n'est en prison."),
                ["jail.offline"] = AdminItemsUi.L("desconectado", "offline", "déconnecté"),
                ["jail.pick"] = AdminItemsUi.L("Elige a quién encarcelar.", "Pick whom to jail.", "Choisissez qui emprisonner."),

                ["error.personaje-desconectado"] = AdminItemsUi.L("Ese personaje no está conectado.", "That character is not connected.", "Ce personnage n'est pas connecté."),
                ["error.personaje-en-combate"] = AdminItemsUi.L("Está en combate.", "They are in a fight.", "Il est en combat."),
                ["error.personaje-ocupado"] = AdminItemsUi.L("Está ocupado; prueba otra vez.", "They are busy; try again.", "Il est occupé ; réessayez."),
                ["error.ya-esta-preso"] = AdminItemsUi.L("Ya está en la cárcel.", "They are in jail already.", "Il est déjà en prison."),
                ["error.no-esta-preso"] = AdminItemsUi.L("No está en la cárcel.", "They are not in jail.", "Il n'est pas en prison."),
                ["error.a-uno-mismo"] = AdminItemsUi.L("No puedes encarcelarte a ti mismo.", "You cannot jail yourself.", "Vous ne pouvez pas vous emprisonner."),
                ["error.sin-carcel"] = AdminItemsUi.L("El servidor no tiene el mapa de la cárcel.", "The server does not have the jail's map.", "Le serveur n'a pas la carte de la prison."),
                ["error.sin-mapa"] = AdminItemsUi.L("No hay mapa en esas coordenadas.", "There is no map at those coordinates.", "Il n'y a pas de carte à ces coordonnées."),
                ["error.mapa-desconocido"] = AdminItemsUi.L("El servidor no conoce ese mapa.", "The server does not know that map.", "Le serveur ne connaît pas cette carte."),
                ["error.npc-invalido"] = AdminItemsUi.L("Ese PNJ no se puede poner (o ya hay uno igual en tu casilla).", "That NPC cannot be put (or the same one stands on your cell).", "Ce PNJ ne peut pas être placé (ou le même est déjà sur votre cellule)."),
                ["error.monstruo-invalido"] = AdminItemsUi.L("El servidor no conoce esos monstruos.", "The server does not know those monsters.", "Le serveur ne connaît pas ces monstres."),
                ["error.miembros-invalidos"] = AdminItemsUi.L("El grupo tiene que tener de uno a ocho monstruos.", "A group has one to eight monsters.", "Un groupe a de un à huit monstres."),
                ["error.no-esta"] = AdminItemsUi.L("Ya no está en el mapa.", "It is no longer on the map.", "Il n'est plus sur la carte."),
                ["error.montura-invalida"] = AdminItemsUi.L("Ese objeto no es una montura.", "That item is not a mount.", "Cet objet n'est pas une monture."),
                ["error.rol"] = AdminItemsUi.L("Esta cuenta no es de administrador.", "This account is not an administrator.", "Ce compte n'est pas administrateur."),
                ["error.sesion"] = AdminItemsUi.L("La sesión ha caducado: vuelve a entrar desde el lanzador.", "The session has expired: sign in again from the launcher.", "La session a expiré : reconnectez-vous depuis le lanceur."),
                ["error.other"] = AdminItemsUi.L("El servidor ha dicho que no: {0}", "The server refused: {0}", "Le serveur a refusé : {0}"),
                ["error.unreachable"] = AdminItemsUi.L("El servidor no contesta: {0}", "The server does not answer: {0}", "Le serveur ne répond pas : {0}"),
            };

        private static string T(string key, params object[] values)
        {
            if (!Texts.TryGetValue(key, out var byLanguage)) return AdminItemsUi.T(key, values);
            string text = byLanguage.TryGetValue(KoliseoUi.Language ?? "en", out string own) ? own : byLanguage["en"];
            return values.Length == 0 ? text : string.Format(CultureInfo.InvariantCulture, text, values);
        }

        // ─── The page's state ────────────────────────────────────────────────────────────

        private static string _shown;
        private static DofusLabel _status;

        /// <summary>The characters in the world, as /api/conectados lists them.</summary>
        private sealed class Player
        {
            public string Name = "";
            public int Level;
            public bool Own, Jailed;
            public long Map;
            public int Cell;
        }

        private static List<Player> _players = new List<Player>();
        private static VisualElement _playersRow;

        /// <summary>Whom the character and teleport tabs act on: empty for one's own.</summary>
        private static string _who = "";

        /// <summary>Whether the player list offers one's own character: not in the jail tab.</summary>
        private static bool _offerOwn = true;

        /// <summary>Everything on the page is gone: a tab switch, or the window closed.</summary>
        public static void Reset()
        {
            _shown = null;
            _status = null;
            _playersRow = null;
            _fields.Clear();
            _where = _cell = _mount = null;
            _search = null;
            _kinds = _list = _detail = _group = _mapList = _prisonList = null;
            _pageLabel = _groupLabel = _mapLabel = null;
        }

        public static VisualElement Build(string tab)
        {
            _shown = tab;
            return tab switch
            {
                "character" => BuildCharacter(),
                "teleport" => BuildTeleport(),
                "spawn" => BuildSpawn(),
                "jail" => BuildJail(),
                _ => new VisualElement(),
            };
        }

        /// <summary>Every frame while the window is open: the spawn search and the jail's clocks.</summary>
        public static void Tick()
        {
            if (_shown == "spawn") TickSearch();
            if (_shown == "jail") TickJail();
        }

        // ─── Pieces ──────────────────────────────────────────────────────────────────────

        /// <summary>A page: a column with the window's margins.</summary>
        private static VisualElement Page()
        {
            var page = new VisualElement();
            page.style.flexGrow = new StyleFloat(1f);
            page.style.paddingLeft = new StyleLength(24f);
            page.style.paddingRight = new StyleLength(24f);
            page.style.paddingTop = new StyleLength(14f);
            page.style.paddingBottom = new StyleLength(16f);
            return page;
        }

        /// <summary>A section's title, in the client's large title style, with a divider under it.</summary>
        private static VisualElement Section(string title)
        {
            var box = new VisualElement();
            box.style.marginTop = new StyleLength(10f);
            box.style.marginBottom = new StyleLength(6f);
            var label = new DofusLabel();
            label.text = title;
            label.AddToClassList("title_large");
            label.AddToClassList("textColor_white_white100");
            box.Add(label);
            try { box.Add(new Divider()); } catch { }
            return box;
        }

        /// <summary>Running text in the client's long-text style, a little dimmed.</summary>
        private static DofusLabel Hint(string text)
        {
            var label = new DofusLabel();
            label.text = text;
            label.AddToClassList("textLong_largeRegular");
            label.AddToClassList("textColor_white_white65");
            label.style.whiteSpace = new StyleEnum<WhiteSpace>(WhiteSpace.Normal);
            label.style.marginBottom = new StyleLength(8f);
            return label;
        }

        /// <summary>
        /// A button of the client's: primary -- gold, for the one that acts -- or secondary. If the
        /// client will not make one, the window's own drawn button instead.
        /// </summary>
        private static VisualElement ClientButton(string text, bool primary, Action click)
        {
            try
            {
                var button = new DofusButtonCustom(AdminItemsUi.Do(click), text, null);
                button.mainStyle = primary ? DofusButtonCustom.ComponentStyleEnum.primary
                                           : DofusButtonCustom.ComponentStyleEnum.secondary;
                button.style.marginRight = new StyleLength(10f);
                button.style.marginBottom = new StyleLength(6f);
                button.style.flexShrink = new StyleFloat(0f);
                return button;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[JondoFix] Admin window: the client's button would not build ({ex.Message}); drawing one.");
                return AdminItemsUi.FilterButton(text, primary, true, click);
            }
        }

        /// <summary>A labelled field: its label above, the client's TextInput under it.</summary>
        private static TextInput Field(VisualElement into, string label, bool numbers, float width = 180f, string placeholder = "")
        {
            var box = new VisualElement();
            box.style.marginRight = new StyleLength(14f);
            box.style.marginBottom = new StyleLength(8f);
            box.style.width = new StyleLength(width);
            box.Add(AdminItemsUi.Label(label));
            var input = new TextInput();
            try { input.isNumbersOnly = numbers; } catch { }
            if (placeholder.Length > 0) input.placeholderText = placeholder;
            box.Add(input);
            into.Add(box);
            return input;
        }

        private static string Read(TextInput input) => (input?.text ?? "").Trim();

        private static DofusLabel Status()
        {
            _status = AdminItemsUi.Label("");
            _status.style.marginTop = new StyleLength(10f);
            _status.style.whiteSpace = new StyleEnum<WhiteSpace>(WhiteSpace.Normal);
            return _status;
        }

        private static void Say(string text)
        {
            if (_status != null) _status.text = text;
        }

        private static VisualElement Flow()
        {
            var row = AdminItemsUi.Row();
            row.style.flexWrap = new StyleEnum<Wrap>(Wrap.Wrap);
            row.style.alignItems = new StyleEnum<Align>(Align.FlexEnd);
            return row;
        }

        // ─── Talking to the server ───────────────────────────────────────────────────────

        /// <summary>
        /// A request to the control API off the frame, and what to do with the answer back on it:
        /// on success the body, otherwise the reason, said on the page.
        /// </summary>
        private static void Ask(string route, Dictionary<string, object> body, Action<JsonElement> then)
        {
            string token = AdminItemsUi.Token();
            if (token.Length == 0)
            {
                Say(AdminItemsUi.T("no_token"));
                return;
            }
            body["token"] = token;
            string page = _shown;
            Say(T("working"));
            Task.Run(async () =>
            {
                var (code, text) = await AdminItemsUi.PostAsync(route, body);
                AdminItemsUi._toUnity.Enqueue(() =>
                {
                    if (_shown != page) return;   // the tab changed meanwhile
                    if (code != 200)
                    {
                        Say(Refusal(code, text));
                        return;
                    }
                    try
                    {
                        using var doc = JsonDocument.Parse(text);
                        Say(T("done"));
                        then(doc.RootElement.Clone());
                    }
                    catch (Exception ex)
                    {
                        Say(T("error.other", ex.Message));
                    }
                });
            });
        }

        private static string Refusal(int code, string body)
        {
            if (code == 0) return T("error.unreachable", body);
            string reason = body;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var error)) reason = error.GetString() ?? body;
            }
            catch { }
            return Texts.ContainsKey("error." + reason) ? T("error." + reason) : T("error.other", reason);
        }

        // ─── Who: the characters in the world ────────────────────────────────────────────

        /// <summary>The row of connected characters to pick from, with its refresh button.</summary>
        private static VisualElement Players(bool offerOwn)
        {
            _offerOwn = offerOwn;
            var box = new VisualElement();
            box.Add(AdminItemsUi.Label(T("who"), true));
            _playersRow = Flow();
            _playersRow.style.marginTop = new StyleLength(4f);
            box.Add(_playersRow);
            RefreshPlayers();
            return box;
        }

        private static void RefreshPlayers()
        {
            Ask("conectados", new Dictionary<string, object>(), root =>
            {
                var players = new List<Player>();
                foreach (var one in root.GetProperty("conectados").EnumerateArray())
                {
                    players.Add(new Player
                    {
                        Name = one.GetProperty("nombre").GetString() ?? "",
                        Level = one.GetProperty("nivel").GetInt32(),
                        Own = one.GetProperty("propio").GetBoolean(),
                        Jailed = one.TryGetProperty("preso", out var j) && j.GetBoolean(),
                        Map = one.TryGetProperty("mapa", out var m) ? m.GetInt64() : 0,
                        Cell = one.TryGetProperty("celda", out var c) ? c.GetInt32() : 0,
                    });
                }
                _players = players;
                ShowPlayers();
            });
        }

        private static void ShowPlayers()
        {
            if (_playersRow == null) return;
            _playersRow.Clear();
            // Whoever was picked and has since left: back to one's own, or to nobody in the jail.
            if (_who.Length > 0 && !_players.Any(p => p.Name == _who)) _who = "";

            foreach (var player in _players)
            {
                if (player.Own && !_offerOwn) continue;
                bool picked = player.Own ? _who.Length == 0 : _who == player.Name;
                string text = $"{player.Name} ({player.Level})" + (player.Own ? " · " + T("me") : "")
                              + (player.Jailed ? " · " + T("jailed") : "");
                var p = player;
                _playersRow.Add(AdminItemsUi.FilterButton(text, picked, true, () =>
                {
                    _who = p.Own ? "" : p.Name;
                    ShowPlayers();
                }));
            }
            _playersRow.Add(ClientButton(T("refresh"), false, RefreshPlayers));
        }

        private static Player Own => _players.FirstOrDefault(p => p.Own);
        private static Player Picked => _who.Length == 0 ? Own : _players.FirstOrDefault(p => p.Name == _who);

        // ─── Character ───────────────────────────────────────────────────────────────────

        private static readonly Dictionary<string, TextInput> _fields = new Dictionary<string, TextInput>();
        private static TextInput _mount;

        /// <summary>The API's key and the text key of each number the character tab sets.</summary>
        private static readonly (string Api, string Text)[] Numbers =
        {
            ("nivel", "char.level"), ("vitalidad", "char.vitality"), ("sabiduria", "char.wisdom"),
            ("fuerza", "char.strength"), ("inteligencia", "char.intelligence"), ("suerte", "char.chance"),
            ("agilidad", "char.agility"), ("kamas", "char.kamas"),
        };

        private static VisualElement BuildCharacter()
        {
            var page = Page();
            page.Add(Players(true));
            page.Add(Section(T("char.title")));
            page.Add(Hint(T("char.hint")));

            var fields = Flow();
            foreach (var (api, text) in Numbers) _fields[api] = Field(fields, T(text), true, 170f);
            page.Add(fields);
            var apply = Flow();
            apply.Add(ClientButton(T("char.apply"), true, ApplyCharacter));
            page.Add(apply);

            var mount = Flow();
            mount.style.marginTop = new StyleLength(14f);
            _mount = Field(mount, T("char.mount"), true, 240f);
            mount.Add(ClientButton(T("char.give_mount"), false, GiveMount));
            page.Add(mount);

            page.Add(Status());
            return page;
        }

        private static void ApplyCharacter()
        {
            var body = new Dictionary<string, object> { ["personaje"] = _who };
            foreach (var (api, _) in Numbers)
            {
                if (long.TryParse(Read(_fields[api]), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
                    body[api] = value;
            }
            if (body.Count == 1)
            {
                Say(T("char.nothing"));
                return;
            }
            Ask("personaje", body, _ =>
            {
                foreach (var field in _fields.Values) field?.SetValueWithoutNotify("");
                RefreshPlayers();
            });
        }

        private static void GiveMount()
        {
            if (!long.TryParse(Read(_mount), out long gid) || gid <= 0) return;
            Ask("personaje", new Dictionary<string, object> { ["personaje"] = _who, ["montura"] = gid },
                _ => _mount?.SetValueWithoutNotify(""));
        }

        // ─── Teleport ────────────────────────────────────────────────────────────────────

        private static TextInput _where, _cell;

        private static VisualElement BuildTeleport()
        {
            var page = Page();
            page.Add(Players(true));
            page.Add(Section(T("tp.title")));

            var fields = Flow();
            _where = Field(fields, T("tp.where"), false, 320f, "-1,0");
            _cell = Field(fields, T("tp.cell"), true, 160f);
            page.Add(fields);

            var buttons = Flow();
            buttons.Add(ClientButton(T("tp.go"), true, Teleport));
            buttons.Add(ClientButton(T("tp.go_to"), false, GoToPicked));
            buttons.Add(ClientButton(T("tp.bring"), false, BringPicked));
            page.Add(buttons);
            page.Add(Status());
            return page;
        }

        private static void Teleport()
        {
            string where = Read(_where).Replace("[", "").Replace("]", "").Replace(" ", "");
            int cell = int.TryParse(Read(_cell), out int c) ? c : -1;
            string who = _who;

            string[] xy = where.Split(',');
            if (xy.Length == 2 && int.TryParse(xy[0], out int x) && int.TryParse(xy[1], out int y))
            {
                Ask("coordenadas", new Dictionary<string, object> { ["x"] = x, ["y"] = y },
                    root => MoveTo(who, root.GetProperty("mapa").GetInt64(), cell));
            }
            else if (long.TryParse(where, out long map) && map > 0) MoveTo(who, map, cell);
            else Say(T("tp.bad"));
        }

        private static void MoveTo(string who, long map, int cell)
        {
            var body = new Dictionary<string, object> { ["personaje"] = who, ["mapa"] = map };
            if (cell >= 0) body["celda"] = cell;
            Ask("personaje", body, _ => RefreshPlayers());
        }

        private static void GoToPicked()
        {
            var target = Picked;
            if (target == null || target.Own) { Say(T("tp.pick_other")); return; }
            MoveTo("", target.Map, target.Cell);
        }

        private static void BringPicked()
        {
            var target = Picked;
            var own = Own;
            if (target == null || target.Own || own == null) { Say(T("tp.pick_other")); return; }
            MoveTo(target.Name, own.Map, own.Cell);
        }

        // ─── Spawn ───────────────────────────────────────────────────────────────────────

        private sealed class Being
        {
            public int Id;
            public string Name = "";
            public string Search = "";
            public bool Npc;
            public List<(int Grade, int Level)> Grades = new List<(int, int)>();
            public int MaxLevel => Grades.Count == 0 ? 0 : Grades.Max(g => g.Level);
        }

        private static List<Being> _npcCatalogue, _monsterCatalogue;
        private static bool _npcsShown;
        private static TextInput _search;
        private static VisualElement _list, _detail, _group, _mapList;
        private static DofusLabel _pageLabel, _groupLabel, _mapLabel;
        private static List<Being> _found = new List<Being>();
        private static int _spawnPage;
        private static Being _being;
        private static readonly List<(Being Monster, int Grade)> _members = new List<(Being, int)>();
        private static string _searchShown, _searchPending;
        private static float _searchSince;
        private const int SpawnPageSize = 30;

        private static VisualElement BuildSpawn()
        {
            EnsureBeings();

            var page = Page();
            var body = AdminItemsUi.Row();
            body.style.flexGrow = new StyleFloat(1f);

            // Left: the client's own catalogue of NPCs and monsters.
            var left = new VisualElement();
            left.style.width = new StyleLength(520f);
            left.style.marginRight = new StyleLength(20f);
            _kinds = AdminItemsUi.Row();
            left.Add(_kinds);
            ShowKinds();
            _search = new TextInput();
            _search.placeholderText = T("spawn.search");
            _search.style.marginTop = new StyleLength(6f);
            _search.style.marginBottom = new StyleLength(6f);
            left.Add(_search);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = new StyleFloat(1f);
            _list = scroll;
            left.Add(scroll);
            var pager = AdminItemsUi.Row();
            pager.style.justifyContent = new StyleEnum<Justify>(Justify.SpaceBetween);
            pager.style.alignItems = new StyleEnum<Align>(Align.Center);
            pager.Add(ClientButton("<", false, () => { _spawnPage--; ShowBeings(); }));
            _pageLabel = AdminItemsUi.Label("");
            pager.Add(_pageLabel);
            pager.Add(ClientButton(">", false, () => { _spawnPage++; ShowBeings(); }));
            left.Add(pager);
            body.Add(left);

            // Right: the one picked, the group being made, and what stands on this map.
            var right = new ScrollView(ScrollViewMode.Vertical);
            right.style.flexGrow = new StyleFloat(1f);
            _detail = new VisualElement();
            right.Add(_detail);
            _groupLabel = AdminItemsUi.Label("", true);
            _groupLabel.style.marginTop = new StyleLength(10f);
            right.Add(_groupLabel);
            _group = new VisualElement();
            right.Add(_group);
            _mapLabel = AdminItemsUi.Label("", true);
            _mapLabel.style.marginTop = new StyleLength(14f);
            right.Add(_mapLabel);
            _mapList = new VisualElement();
            right.Add(_mapList);
            right.Add(Status());
            body.Add(right);

            page.Add(body);

            _searchShown = null;
            _searchPending = "";
            ShowBeings("");
            ShowBeing();
            ShowGroup();
            RefreshMap();
            return page;
        }

        private static VisualElement _kinds;

        /// <summary>The two chips that switch the list between monsters and NPCs.</summary>
        private static void ShowKinds()
        {
            if (_kinds == null) return;
            _kinds.Clear();
            _kinds.Add(AdminItemsUi.FilterButton(T("spawn.monsters"), !_npcsShown, true, () => PickKind(false)));
            _kinds.Add(AdminItemsUi.FilterButton(T("spawn.npcs"), _npcsShown, true, () => PickKind(true)));
        }

        private static void PickKind(bool npcs)
        {
            _npcsShown = npcs;
            _being = null;
            ShowKinds();
            ShowBeings(Read(_search));
            ShowBeing();
        }

        /// <summary>Whether a monster's levels meet a range; an NPC, or no range, always does.</summary>
        private static bool LevelFits(Being being, int min, int max)
        {
            if (being.Npc || (min == int.MinValue && max == int.MaxValue)) return true;
            if (being.Grades.Count == 0) return false;
            return being.MaxLevel >= min && being.Grades.Min(g => g.Level) <= max;
        }

        private static void EnsureBeings()
        {
            if (_monsterCatalogue == null)
            {
                var monsters = new List<Being>();
                try
                {
                    var all = DataCenterModule.monstersDataRoot?.GetObjects();
                    for (int i = 0; all != null && i < all.Count; i++)
                    {
                        var monster = all[i];
                        if (monster == null || string.IsNullOrEmpty(monster.name)) continue;
                        var being = new Being { Id = monster.id, Name = monster.name, Npc = false };
                        var grades = monster.grades;
                        for (int g = 0; grades != null && g < grades.Count; g++)
                            if (grades[g] != null) being.Grades.Add((grades[g].grade, grades[g].level));
                        being.Search = AdminItemsUi.Fold(being.Name + " " + being.Id.ToString(CultureInfo.InvariantCulture));
                        monsters.Add(being);
                    }
                }
                catch (Exception ex) { MelonLogger.Warning($"[JondoFix] Admin window: the monsters: {ex.Message}"); }
                _monsterCatalogue = monsters.OrderBy(m => m.MaxLevel).ThenBy(m => m.Name).ToList();
            }
            if (_npcCatalogue == null)
            {
                var npcs = new List<Being>();
                try
                {
                    var all = DataCenterModule.npcsDataRoot?.GetObjects();
                    for (int i = 0; all != null && i < all.Count; i++)
                    {
                        var npc = all[i];
                        if (npc == null || string.IsNullOrEmpty(npc.name)) continue;
                        npcs.Add(new Being
                        {
                            Id = npc.id, Name = npc.name, Npc = true,
                            Search = AdminItemsUi.Fold(npc.name + " " + npc.id.ToString(CultureInfo.InvariantCulture)),
                        });
                    }
                }
                catch (Exception ex) { MelonLogger.Warning($"[JondoFix] Admin window: the NPCs: {ex.Message}"); }
                _npcCatalogue = npcs.OrderBy(n => n.Name).ToList();
            }
        }

        private static void TickSearch()
        {
            if (_search == null) return;
            string typed = _search.text ?? "";
            if (typed != _searchPending)
            {
                _searchPending = typed;
                _searchSince = UnityEngine.Time.unscaledTime;
            }
            if (typed != _searchShown && UnityEngine.Time.unscaledTime - _searchSince >= 0.25f) ShowBeings(typed);
        }

        private static void ShowBeings(string typed)
        {
            _searchShown = typed;
            var words = new List<string>();
            int min = int.MinValue, max = int.MaxValue;
            foreach (string word in AdminItemsUi.Fold(typed ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int dash = word.IndexOf('-');
                if (dash > 0 && int.TryParse(word.Substring(0, dash), out int a) && int.TryParse(word.Substring(dash + 1), out int b))
                {
                    min = Math.Min(a, b);
                    max = Math.Max(a, b);
                }
                else words.Add(word);
            }

            var source = _npcsShown ? _npcCatalogue : _monsterCatalogue;
            _found = (source ?? new List<Being>())
                .Where(b => LevelFits(b, min, max))
                .Where(b => words.All(w => b.Search.Contains(w)))
                .ToList();
            _spawnPage = 0;
            ShowBeings();
        }

        private static void ShowBeings()
        {
            if (_list == null) return;
            int pages = Math.Max(1, (_found.Count + SpawnPageSize - 1) / SpawnPageSize);
            _spawnPage = Math.Clamp(_spawnPage, 0, pages - 1);
            if (_pageLabel != null) _pageLabel.text = T("spawn.page", _spawnPage + 1, pages, _found.Count);
            _list.Clear();
            int end = Math.Min(_found.Count, (_spawnPage + 1) * SpawnPageSize);
            for (int i = _spawnPage * SpawnPageSize; i < end; i++)
            {
                var being = _found[i];
                string levels = being.Grades.Count == 0 ? "" : $"  ·  {being.Grades.Min(g => g.Level)}-{being.MaxLevel}";
                var row = AdminItemsUi.FilterButton($"{being.Name}  ·  {being.Id}{levels}", being == _being, false, () =>
                {
                    _being = being;
                    ShowBeing();
                    ShowBeings();
                });
                row.style.marginRight = new StyleLength(0f);
                _list.Add(row);
            }
        }

        private static void ShowBeing()
        {
            if (_detail == null) return;
            _detail.Clear();
            if (_being == null)
            {
                _detail.Add(Hint(T("spawn.pick")));
                return;
            }

            _detail.Add(Section($"{_being.Name} ({_being.Id})"));
            var actions = Flow();
            if (_being.Npc)
            {
                actions.Add(ClientButton(T("spawn.npc"), true, () =>
                    Ask("invocar", new Dictionary<string, object> { ["tipo"] = "npc", ["npc"] = _being.Id }, ShowMap)));
            }
            else
            {
                foreach (var (grade, level) in _being.Grades.OrderBy(g => g.Grade))
                {
                    var monster = _being;
                    int g = grade;
                    actions.Add(ClientButton(T("spawn.grade", grade, level), false, () =>
                    {
                        if (_members.Count >= 8) { Say(T("spawn.full")); return; }
                        _members.Add((monster, g));
                        ShowGroup();
                    }));
                }
            }
            _detail.Add(actions);
        }

        private static void ShowGroup()
        {
            if (_group == null) return;
            _group.Clear();
            if (_groupLabel != null) _groupLabel.text = T("spawn.group", _members.Count);
            for (int i = 0; i < _members.Count; i++)
            {
                int index = i;
                var (monster, grade) = _members[i];
                int level = monster.Grades.FirstOrDefault(g => g.Grade == grade).Level;
                _group.Add(AdminItemsUi.FilterButton($"{monster.Name} · {T("spawn.grade", grade, level)}  ✕", false, false, () =>
                {
                    if (index < _members.Count) _members.RemoveAt(index);
                    ShowGroup();
                }));
            }
            if (_members.Count > 0)
            {
                var buttons = Flow();
                buttons.Add(ClientButton(T("spawn.group_go"), true, SpawnGroup));
                buttons.Add(ClientButton(T("spawn.clear"), false, () => { _members.Clear(); ShowGroup(); }));
                _group.Add(buttons);
            }
        }

        private static void SpawnGroup()
        {
            var members = _members.Select(m => (object)new Dictionary<string, object> { ["monstruo"] = m.Monster.Id, ["grado"] = m.Grade }).ToList();
            Ask("invocar", new Dictionary<string, object> { ["tipo"] = "monstruos", ["miembros"] = members }, root =>
            {
                _members.Clear();
                ShowGroup();
                ShowMap(root);
            });
        }

        private static void RefreshMap() => Ask("mapa", new Dictionary<string, object>(), ShowMap);

        /// <summary>What stands on the admin's map, each with its button to take it off.</summary>
        private static void ShowMap(JsonElement root)
        {
            if (_mapList == null) return;
            _mapList.Clear();
            long map = root.GetProperty("mapa").GetInt64();
            if (_mapLabel != null) _mapLabel.text = T("spawn.here", map);

            int count = 0;
            foreach (var npc in root.GetProperty("npcs").EnumerateArray())
            {
                long id = npc.GetProperty("id").GetInt64();
                int npcId = npc.GetProperty("npc").GetInt32();
                string name = _npcCatalogue?.FirstOrDefault(n => n.Id == npcId)?.Name ?? npcId.ToString(CultureInfo.InvariantCulture);
                _mapList.Add(MapLine($"{T("spawn.npcs")}: {name} ({npcId}) · {T("spawn.cell", npc.GetProperty("celda").GetInt32())}",
                    () => Ask("quitar", new Dictionary<string, object> { ["tipo"] = "npc", ["id"] = id }, ShowMap)));
                count++;
            }
            foreach (var group in root.GetProperty("grupos").EnumerateArray())
            {
                long id = group.GetProperty("id").GetInt64();
                var names = new List<string>();
                foreach (var member in group.GetProperty("miembros").EnumerateArray())
                {
                    int monsterId = member.GetProperty("monstruo").GetInt32();
                    string name = _monsterCatalogue?.FirstOrDefault(m => m.Id == monsterId)?.Name ?? monsterId.ToString(CultureInfo.InvariantCulture);
                    names.Add($"{name} {member.GetProperty("nivel").GetInt32()}");
                }
                _mapList.Add(MapLine($"{T("spawn.monsters")}: {string.Join(", ", names)} · {T("spawn.cell", group.GetProperty("celda").GetInt32())}",
                    () => Ask("quitar", new Dictionary<string, object> { ["tipo"] = "grupo", ["id"] = id }, ShowMap)));
                count++;
            }
            if (count == 0) _mapList.Add(Hint(T("spawn.empty")));
            _mapList.Add(ClientButton(T("refresh"), false, RefreshMap));
        }

        private static VisualElement MapLine(string text, Action remove)
        {
            var line = AdminItemsUi.Row();
            line.style.alignItems = new StyleEnum<Align>(Align.Center);
            line.style.marginBottom = new StyleLength(4f);
            var label = AdminItemsUi.Label(text);
            label.style.flexGrow = new StyleFloat(1f);
            label.style.flexShrink = new StyleFloat(1f);
            line.Add(label);
            line.Add(ClientButton(T("spawn.remove"), false, remove));
            return line;
        }

        // ─── Jail ────────────────────────────────────────────────────────────────────────

        private sealed class Prisoner
        {
            public string Name = "";
            public float OutAt;
            public bool Online;
            public DofusLabel Clock;
        }

        private static VisualElement _prisonList;
        private static List<Prisoner> _prisoners = new List<Prisoner>();
        private static float _jailAskedAt, _jailDrawnAt;

        private static VisualElement BuildJail()
        {
            var page = Page();
            page.Add(Players(false));
            page.Add(Section(T("jail.title")));
            page.Add(Hint(T("jail.hint")));
            var send = Flow();
            send.Add(ClientButton(T("jail.send"), true, () =>
            {
                if (_who.Length == 0) { Say(T("jail.pick")); return; }
                Ask("carcel", new Dictionary<string, object> { ["personaje"] = _who }, _ => { RefreshPrisoners(); RefreshPlayers(); });
            }));
            page.Add(send);

            page.Add(Section(T("jail.inside")));
            _prisonList = new VisualElement();
            page.Add(_prisonList);
            page.Add(Status());
            RefreshPrisoners();
            return page;
        }

        private static void RefreshPrisoners()
        {
            _jailAskedAt = UnityEngine.Time.unscaledTime;
            Ask("presos", new Dictionary<string, object>(), root =>
            {
                float now = UnityEngine.Time.unscaledTime;
                _prisoners = root.GetProperty("presos").EnumerateArray().Select(p => new Prisoner
                {
                    Name = p.GetProperty("nombre").GetString() ?? "",
                    OutAt = now + p.GetProperty("quedan").GetInt32(),
                    Online = p.GetProperty("conectado").GetBoolean(),
                }).ToList();
                ShowPrisoners();
            });
        }

        private static void ShowPrisoners()
        {
            if (_prisonList == null) return;
            _prisonList.Clear();
            if (_prisoners.Count == 0) _prisonList.Add(Hint(T("jail.nobody")));
            foreach (var prisoner in _prisoners)
            {
                var line = AdminItemsUi.Row();
                line.style.alignItems = new StyleEnum<Align>(Align.Center);
                line.style.marginBottom = new StyleLength(4f);
                var name = AdminItemsUi.Label(prisoner.Name + (prisoner.Online ? "" : $" ({T("jail.offline")})"), true);
                name.style.width = new StyleLength(320f);
                line.Add(name);
                prisoner.Clock = AdminItemsUi.Label("");
                prisoner.Clock.style.width = new StyleLength(90f);
                line.Add(prisoner.Clock);
                var p = prisoner;
                line.Add(ClientButton(T("jail.release"), false, () =>
                    Ask("liberar", new Dictionary<string, object> { ["personaje"] = p.Name }, _ => { RefreshPrisoners(); RefreshPlayers(); })));
                _prisonList.Add(line);
            }
            DrawClocks();
        }

        /// <summary>The clocks count down every second; the list is asked again every ten.</summary>
        private static void TickJail()
        {
            float now = UnityEngine.Time.unscaledTime;
            if (now - _jailDrawnAt >= 1f) DrawClocks();
            if (now - _jailAskedAt >= 10f) RefreshPrisoners();
        }

        private static void DrawClocks()
        {
            _jailDrawnAt = UnityEngine.Time.unscaledTime;
            foreach (var prisoner in _prisoners)
            {
                if (prisoner.Clock == null) continue;
                int left = Math.Max(0, (int)Math.Ceiling(prisoner.OutAt - _jailDrawnAt));
                prisoner.Clock.text = $"{left / 60}:{left % 60:00}";
            }
        }
    }
}
