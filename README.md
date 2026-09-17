High-performance server emulator for **Dofus 3 Unity (Client 3.6.10.11)** written in C# (**.NET 10**), with decoupled modular projects, a SQLite data layer, a combat engine driven entirely by client data — PvM, duels and Koliseo — a cross-platform launcher and a world editor.

> ⚠️ **Runs against Dofus 3 clients 3.6.10.11 and 3.6.10.10.** The live game is already on
> 3.6.11.12, so the official launcher will not hand you a client that works here —
> [Step 2](#step-2--get-the-361011-client) has a download. Ankama renames every protobuf message to
> three random letters on some patches; there is a toolchain here for surviving that — see
> [Surviving the next patch](#-surviving-the-next-patch).

---

## 📑 Contents

| 🖥️ [Launcher](#-launcher) | 🧩 [Server](#-server) | 🛠️ [Jondo Studio](#-jondo-studio) |
|:---|:---|:---|
| The player's window, in Avalonia. A team of up to eight accounts, each with its character drawn from the client's own bones. | The emulator itself. Four listeners in one process, one session per socket, and guards that refuse to boot on bad data. | The world editor. Nine sections over the client's data, writing a reviewable diff instead of a 240 MB binary. |

&nbsp;

> **New here?** [Quick Start](#-quick-start) puts you in the game in four steps ·
> [What you get](#-what-you-get) is what lands on disk

&nbsp;

- 🌍 &nbsp;**World** &nbsp;— &nbsp;[Connection and authentication](#-connection-and-authentication) · [World and movement](#-world-and-movement) · [Travel](#-travel) · [Houses, bins and haven bags](#-houses-bins-and-haven-bags) · [Social](#-social)

- 🎒 &nbsp;**Character** &nbsp;— &nbsp;[Character and inventory](#-character-and-inventory) · [Appearances](#-appearances) · [Professions](#-professions)

- 📚 &nbsp;**Content** &nbsp;— &nbsp;[NPCs and monsters](#-npcs-and-monsters) · [Quests](#-quests) · [Dungeons](#-dungeons) · [Jondo Coin](#-jondo-coin)

- ⚔️ &nbsp;**Combat** &nbsp;— &nbsp;[One engine, three rulebooks](#-one-engine-three-rulebooks) · [PvM](#-pvm-combat) · [Duels](#-duels) · [Koliseo](#-koliseo) · [Spell effect engine](#-spell-effect-engine) · [Spell check-list](#-spell-check-list) · [Combat challenges](#-combat-challenges) · [Not implemented](#-not-implemented-at-all)

- 🔎 &nbsp;**Tools** &nbsp;— &nbsp;[Jondo Studio](#-jondo-studio) · [Surviving the next patch](#-surviving-the-next-patch)

- 🧱 &nbsp;**Under the hood** &nbsp;— &nbsp;[Tests](#-tests) · [Source layout](#-source-layout) · [Database and persistence](#-database-and-persistence)

---

## 🚀 Quick Start

**Nothing has to be compiled.** The launcher ships as a single ready-to-run executable with every dependency inside it, and the world database ships compressed and extracts itself on first run.

### Step 1 — Install the .NET 10 runtime

Download it from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0). The *Desktop Runtime* is the one you want.

### Step 2 — Get the 3.6.10.11 client

**You can no longer just use your installed Dofus.** The live game is on 3.6.11.12 and Ankama's
launcher only ever gives you the current version, which this emulator does not speak.

**⬇️ [Dofus 3.6.10.11 — download](https://www.swisstransfer.com/dl/01a082ed-3e6a-70b9-987b-7f2551484389)**

It is the stock Ankama client, untouched — Step 3 is what makes it talk to the emulator. Unpack it
as a **`Cliente 3.6.10.11`** folder beside the emulator folder, which is where the launcher looks
first; anywhere else is fine too if you point **Settings** at your `Dofus.exe`.

If you still have a 3.6.10.11 or 3.6.10.10 install from before the patch, that one works — just
keep the official launcher from updating it.

### Step 3 — Point the Dofus client at the emulator

The official client talks to Ankama's servers and checks their SSL certificates. **JondoFix**, a MelonLoader mod, redirects it to your machine instead. It comes already built in this repository.

1. Get **MelonLoader 0.7.x** from [its releases page](https://github.com/LavaGang/MelonLoader/releases). **Read this bit or you will pick the wrong one:** 0.7.x is published as *Open-Beta*, so it shows up as a **pre-release** and the page's "Latest" tag still points at 0.6.x. **0.6.x does not work with this client** — tick *show pre-releases* and take 0.7.x. The setup this repository is tested against runs **0.7.3**.
2. Run the installer and point it at your **`Dofus.exe`**. That is the only thing you have to choose: MelonLoader works out the rest by itself. On this client it reports `Game Type: Il2cpp`, `Game Arch: x64`, `Runtime Type: net6`, Unity `6000.3.16f1` — you do not set any of that.
3. Copy **`JondoFix/JondoFix.dll`** from this repository into the **`Mods/`** folder of your Dofus installation, next to `Dofus.exe`. MelonLoader creates that folder the first time the game starts; if it is not there yet, just create it yourself.

> The mod ships **already compiled** and is the exact binary in use — you never need to build it. `JondoFix/` also carries its source, in case you want to read or change it.

Two things worth knowing afterwards:
* The installer drops a **`version.dll`** next to `Dofus.exe`; that is what loads MelonLoader. Renaming it to `version.dll.disabled` turns the whole thing off so you can play the official game, and renaming it back turns it on again — no need to uninstall anything.
* MelonLoader writes a log per run under **`MelonLoader/Logs/`**. If the client starts but never reaches the emulator, that file is the first place to look.

What JondoFix does: intercepts sockets, Named Pipes and DNS queries and sends them to `localhost` (ports `8888`, `5555`, `15881`, `6337`); stops HTTPS requests from failing against the local self-signed certificate; and injects the environment variables the client expects (`ZAAP_PORT`, `ZAAP_HASH`, and so on).

### Step 4 — Run it

Double-click **`Jondo Emulator Launcher.exe`**. That is the only thing you start by hand: it launches **`Jondo Server.exe`** itself, in its own window with the log and the counters.

On the first run it unpacks `datos/world.zip` into `bases/world.db` (about 240 MB, it takes a moment) and creates `bases/auth.db` with a test account. Sign in to add an account to the launcher's team, tick one or several saved profiles, then press **Launch selected**. Up to eight independent Dofus clients can be active at once.

```
Account: keka
Password: test
```

By default the emulator looks for the client next to itself, in a `Cliente 3.6.10.11` folder beside the emulator folder — or `Cliente 3.6.10.10`, whichever it finds first. If yours lives somewhere else, set it in **Settings** and point it at your `Dofus.exe`. The choice is remembered, and if the client later moves the launcher says so instead of failing silently.

The **ES / EN / FR** switch sets the language of the launcher *and* of the game: the client is started with that `--langCode`.

**`Jondo Studio.exe`** is the third executable and needs nothing else running: double-click it whenever you want to look at the world or build content. See [Jondo Studio](#-jondo-studio) below.

---

## 📂 What you get

```
Jondo Emulator Launcher.exe   ← this is what you run
Jondo Server.exe              the server; the launcher starts it
Jondo Studio.exe              the world editor; open it when you want to look or build
content/                      the only files a person edits by hand, versioned in git
datos/                        json and bin the emulator reads (maps, items, appearances, zaaps…)
bases/                        writable databases and five verified pre-migration backup sets
docs/                         technical documentation
launcher_assets/              launcher artwork and music
JondoFix/                     the MelonLoader mod, source and compiled dll
Jondo.Unity.*/                source code
```

`content/` **is** in the repository, deliberately: it is the only folder a person edits by hand, it is small, and a change in it is a reviewable diff.

Important player and administrator actions are also written as one JSON object per line in `logs/activity.jsonl`. Commands, equipment moves, lottery prizes, granted items, fights, live administration and new unhandled packet shapes can therefore be filtered without scraping the human-readable console log. Credentials, launcher tokens and game tickets are never included.

Not in the repository because they are not needed to play: `bases/` (built on first run), `logs/`, `tools/` (the Python that regenerates `datos/`) and `dofus3_data/` (436 MB of raw client dump, only used by those tools).

---

## ✅ Emulation status

✅ done · 🟡 partial · 🚧 in progress · ❌ missing

### 🖥️ Launcher

<img width="2560" height="1512" alt="image" src="https://github.com/user-attachments/assets/68e0e721-b36c-4524-b5d6-660fd5beb3c0" />
<img width="2560" height="1504" alt="image" src="https://github.com/user-attachments/assets/86f835e0-f161-4f51-af42-a810a192f150" />

Rewritten in **Avalonia**, the same toolkit as the Studio. It used to be Windows Forms, drawn from code; nothing but the music is tied to the Windows desktop any more.

- ✅ **Three screens instead of one wall of buttons** — *Play*, *Accounts*, *Settings*, with the server-status pill in the header
- ✅ **Account cards with the character drawn in them** — portrait, name, level and a big tick. The portrait is assembled from the **client's own bones**, exactly the way Jondo Studio draws NPCs: not one image ships inside the executable
- ✅ The portrait shows the character **as they look in the world** — the chosen head, the real equipment and the cosmetics over it, in the same skin list the game client is sent
- ✅ Persistent team of up to 8 accounts, one independent Dofus process each; the highest-level character of each account is the one shown
- ✅ Account creation and login, written straight to `auth.db`; credentials sealed with DPAPI
- ✅ Per-client identity chain — instance id, launch hash, Zaap session, game token, single-use ticket, socket-owned session
- ✅ Independent lifecycle indicators for profiles, processes and sockets
- ✅ Embedded server log; single-file deployment; ES/EN/FR
- ✅ A neon sign that **starts like a real tube** — a hand-written stutter sequence, then a steady glow with the occasional flicker — and falling stars behind it. The choreography is written down rather than random on purpose: random timings read as a broken light, not a starting one
- ✅ Launcher and server are separate programs — the launcher carries no database, maps, handlers or effect catalogue
- 🚧 **OAuth is wired up and waiting for the website** — loopback redirect and PKCE on the launcher side; the server half is deliberately unwritten until there is a site to talk to

### 🧩 Server

<img width="2558" height="1508" alt="image" src="https://github.com/user-attachments/assets/df3cce87-166d-4f5a-8aff-a4fcd2575c87" />

`Jondo Server.exe`. The launcher starts it, but it is a program in its own right and can be run on
its own — or on another machine.

- ✅ Four listeners in one process — Zaap (`8888`), game (`5555`), chat (`6337`) and HAAPI
  (`15881`), plus a self-signed certificate so the client's HTTPS does not fail
- ✅ **One session per socket**, not one per account: every handler reads the session it is
  serving, so eight clients on one machine never see each other's state
- ✅ Its own window with the live log, the counters and the connected clients
- ✅ **Regression guards that run at boot and refuse to start** when the shipped data does not match
  what the code expects — see [Tests](#-tests)
- ✅ A loopback **control API** the launcher talks to: log tail, account login, and the characters
  of an account with the look already composed for drawing
- ✅ **Runs on another machine.** Every listener honours `JONDO_PUBLIC_BIND`, and the launcher runs a
  loopback relay so the client reaches it. The relay is not a convenience: HAAPI and the chat server
  both hand the client `127.0.0.1`, so repointing the client at a remote host cannot work on its own
- ✅ Unanswerable packets are recorded in their own database, deduplicated by protobuf shape

### 🔐 Connection and authentication

- ✅ Zaap, HAAPI and connection server emulation, VIP check bypassed
- ✅ Account creation and login against `auth.db`, with the password hashed and the attempt rate
  limited **by the socket's own IP** — taking it from the request body meant one JSON field made the
  limiter useless
- ✅ Per-client identity chain — instance id, launch hash, Zaap session, game token, single-use
  ticket, socket-owned session
- ✅ Server and character selection, showing the mount being ridden and each character's equipment
  
<img width="2560" height="1500" alt="image" src="https://github.com/user-attachments/assets/c4c194ad-dcd1-407f-a3f1-b44c8f4baed2" />
<img width="2558" height="1504" alt="image" src="https://github.com/user-attachments/assets/70d02ad2-8fc0-4ec8-b836-1dda959ed271" />

- ✅ Character creation with a starter kit — Astrub zaap, adventurer set, 1,000,000 kamas, 101
  scrolled points per characteristic
  
<img width="2560" height="1504" alt="image" src="https://github.com/user-attachments/assets/881c9530-6631-46ed-b85e-c7fd92602455" />
<img width="2560" height="1502" alt="image" src="https://github.com/user-attachments/assets/09a94e1f-165a-407d-91f1-1dd7b18063de" />
<img width="2558" height="1510" alt="image" src="https://github.com/user-attachments/assets/a65407d0-7e65-4481-bdfe-ea65554bb29e" />

- ✅ Account roles, and an administrator-only channel over loopback
- ✅ **Reconnecting into a fight.** Close the client mid-fight and the fight goes on without you: a
  write to a dead socket is dropped, not thrown. Log back in and the welcome burst stops short of
  the character list; the client asks for it (`kvc`) and receives it with an empty `kvd` behind —
  *do not stop here* — which it answers with `kwb`, and the character still fighting is picked
  without a selection screen. Its map block goes out in the fight flavour (`kmp` type 1, the arena,
  *back in the fight*) and, once the client says it is ready, the board as it stands: the
  in-progress `kaa`, every fighter, the live buffs, and the current turn with the time it has left.
  Measured against the two reconnection captures. Picking a character the ordinary way while it has
  a fight pending is turning that fight down — whoever is still inside sees a surrender, and the
  character enters the world on the roleplay map it left, not on the arena

### 🗺️ World and movement
- ✅ World loading, spawn, name hover, last cell and map persisted. Multiclient.
<img width="2560" height="1506" alt="image" src="https://github.com/user-attachments/assets/8882de29-36b0-4af9-be22-2d5f3bd4c6d4" />

- ✅ **15,360 maps**, **17,211** with walkable-cell data, **17,222** with combat cells
- ✅ Movement, map change and adjacent maps; auto-pilot from the minimap and *travel to*
<img width="538" height="452" alt="image" src="https://github.com/user-attachments/assets/a6438938-00c2-4a76-b4e1-48abf3d56934" />

- ✅ Seeing others arrive and leave, in all four directions
- ✅ Up to 8 clients at once, each on its own socket-owned session
- ✅ **Everybody is drawn wearing their gear** — the other players on the map, the opponent in a fight and every character on the selection screen. Equipment is read per character from `CharacterItems`, so it never depends on who happens to be connected

### 🌀 Travel

- ✅ **62 waypoints** with map, cell and sub-area, plus 3 departure-only zaaps the waypoint table omits
- ✅ Travel between zaaps with the real cost and destination list
<img width="2560" height="1502" alt="image" src="https://github.com/user-attachments/assets/1524d485-a845-4b62-a71c-b88de3bb7b54" />

- ✅ Discovered zaaps announced on world entry (`hjk`) — without it the travel window reads "No destination"
- ✅ Zaapis of Bonta (24) and Brakmar (21) at a flat 20 kamas, read off captures because client data cannot derive them
<img width="2560" height="1484" alt="image" src="https://github.com/user-attachments/assets/472e09f2-a49d-431e-8935-f60355457cdd" />

- ✅ The right window per list: `hjj` root field 0 zaap, 1 zaapi, 3 boat
- ✅ **16 temporal anomalies** with their 120-minute countdown, surfacing at vestiges (type 359), not at switched-off zaaps
<img width="2560" height="1514" alt="image" src="https://github.com/user-attachments/assets/942d4d71-9711-45f1-9156-5381f7ad14b8" />

- ✅ **3,815 interactive teleports** imported, 3,719 active across 2,655 maps
- ✅ **Passages that fire when you step on the cell**, hooked to the end of a walk rather than to the map edge — which is what the ground-level exits need
- ✅ Each route carries **its own measured interactive type** instead of a forced zero. The type is part of the element's identity on the client side: with a zero the numbers still travel but the client stops attaching the declaration to the drawing, and the exit sun disappears
- 🟡 **Every extracted passage still declares skill 114**, which is *Utilizar* on a zaap. Measured three ways that agree: Ankama's own world graph uses **184** on 5,629 of 5,719 interactive transitions and 114 on none; over 401 captures 184 appears on 420 elements and 114 on 23, every one a zaap; and in our own traffic skill 184 is followed by a map change 178 times while 114 opens the zaap window. New passages written in Jondo Studio declare 184; the extracted rows have not been rewritten
- ✅ **New passages can be created**, both ways, from Jondo Studio — which is what makes a house with its own interior possible
<img width="2560" height="1506" alt="image" src="https://github.com/user-attachments/assets/e3908060-2ad1-415c-a11a-cb6f323b9378" />

### 🏘️ Houses, bins and haven bags

- ✅ **1,437 doors on 553 maps**, all enterable and ownerless; **261 house models** with name, price and room count
<img width="1112" height="920" alt="image" src="https://github.com/user-attachments/assets/1506283c-f6cd-45b5-b9c4-f345273f67bb" />

- ✅ Entering and leaving, which are different messages (`jqw` in, `jru` out), coming out through the door you went in by
- ❌ The house plaque, chest, access code, buying and selling
- ✅ **67 public bins on 63 maps** — they open, show empty and close
- ❌ Putting items into a bin and taking them out
- ✅ Haven bags: entering and leaving, their own zaap, **48 themes**, **4,083 furniture pieces** placed and persisted, chest with the full item flow, lottery machine, and no monsters inside
<img width="2560" height="1492" alt="image" src="https://github.com/user-attachments/assets/a81a3b24-8559-4ad5-8a27-e6913eef95a8" />

> Which house sits behind which door is **not in the client**. The 1,437 doors share **114 genuine interiors**, assigned deterministically and kept inside their own neighbourhood; the mapping lives in `datos/casas_mundo_3.6.10.10.json` and can be corrected by hand.

### 💬 Social

- ✅ Information messages as `lqn { type, message, parameters }` against the client's 2,555-entry table, not as chat text
- ✅ Level-up window with music and animation, on a real gain and on `.level` in either direction
<img width="2560" height="1514" alt="image" src="https://github.com/user-attachments/assets/490997fc-1300-4a29-9963-32077efdf0dd" />

- ✅ Private messages via `kth`, which the client routes by opcode and not by channel
- ✅ Last connection time and IP, stored per character
- ✅ Parties — invite, accept, refuse, leave, hand over the lead, kick, and a full member sheet
- ✅ Lead passes on when the leader leaves; a disconnect removes the member and tells the rest
- ✅ Friends list
- ✅ **Every command answers in the session's own language**, from a 48-key catalogue in Spanish, English and French. The language comes from the `--langCode` the launcher started the client with, not from the wire: measured over the nine authentication captures, the client does send its two-letter code, but in `kqz` field 3
- ❌ The invitation popup's *Details* button (`imd` → `ilb`), the dedicated member-gone message (`inc`), party search and following the leader

### 🎒 Character and inventory

- ✅ **21,748 item templates** and **66,294 item effects** — spawning, equipping, bags, destruction, persistence
- ✅ **929 item sets** with their bonuses
- ✅ **520 mounts** with their look, swapped and unequipped correctly
<img width="2560" height="1500" alt="image" src="https://github.com/user-attachments/assets/375da573-ab61-4bf0-83fd-6f2a8f872cde" />
<img width="2560" height="1506" alt="image" src="https://github.com/user-attachments/assets/581b105c-9569-4f54-ab77-01e122b8ce06" />

- ✅ Characteristic assignment, dynamic capital, points in sync across every client panel
<img width="708" height="1048" alt="image" src="https://github.com/user-attachments/assets/b07f0ac2-f701-4f3a-82e2-c04f884d696d" />

- ✅ **17,113 spells** across **34,823 spell levels**; **638 character heads**
<img width="2560" height="1508" alt="image" src="https://github.com/user-attachments/assets/02b5e575-20e3-47ed-b095-55443fb792ab" />

- ✅ **539 titles** and **167 ornaments**, applied, persisted and carried in the map actor block
<img width="2560" height="1498" alt="image" src="https://github.com/user-attachments/assets/0e579800-2776-4aba-8629-58bb2e6c7acf" />
<img width="2560" height="1502" alt="image" src="https://github.com/user-attachments/assets/25225e6b-2b6e-4aa6-8f56-12937b0754a0" />

- ✅ **Life regeneration**, run by the client and switched by the server: `ktz` starts it behind
  every return to a roleplay map (143 messages across 105 captures) and `kuq` stops it on the way
  into a fight — the life reached, the half-second ticks elapsed and the maximum — between the
  `lqu` and the `lva` of the tactical map (97 across 69), so a fight starts on the life the ticks
  earned instead of one the client keeps counting up. The `lqg`+`lqt` pair is not part of it: it
  is the server's 240-second probe, answered with `lqc`+`lqf`
- ✅ Commands — `.teleport`, `.kamas`, `.shop`, `.size`, `.level`, `.item`, `.itemset`
- ✅ **Live administration over HTTP** — `POST /api/personaje` sets characteristics, kamas and level, grants items or a mount, and teleports a connected character without a reconnect. `POST /api/rol` changes account roles. Administrator only, loopback only, and serialized with the target session
- 🟡 `.level` repaints the in-fight spell bar, but the fighter's own level is not updated, so the engine still resolves spells at the level the fight started with

### 👕 Appearances
<img width="2560" height="1508" alt="image" src="https://github.com/user-attachments/assets/30ee645b-191b-4146-9966-d2c3fb72a9cf" />
<img width="2560" height="1500" alt="image" src="https://github.com/user-attachments/assets/2655dfd1-d565-484c-9735-54dd00f4f8b0" />

Dofus does not ship the item-to-look table: the server sends it. **2,371 of the 2,420 cosmetics** in the catalogue were measured off captures, one garment at a time.

| Type | Working / catalogue | | Type | Working / catalogue |
|---|---:|---|---|---:|
| Shields | 524 / 524 | | Petmounts | 151 / 151 |
| Hats | 464 / 464 | | Mounts | 121 / 121 |
| Capes | 357 / 357 | | Shoulders | 121 / 121 |
| Pets | 242 / 242 | | Costumes | 92 / 92 |
| Weapons | 194 / 194 | | Living objects | 61 / 61 |
| Wings | 44 / 44 | | Miscellaneous | 0 / 49 |

- ✅ Appearance weapons carry no look by design — the client draws them; the server only remembers which of the 10 weapon slots each occupies
- ✅ Living objects imitate a different garment per variant, stored as **543 object/variant pairs** across 10 slots
- ✅ Mount and pet appearances are mutually exclusive, matching the real server
- ✅ **The real equipment renders too, and a cosmetic replaces it rather than stacking on top.** **741 real items** carry their own skin into the look; the slots a visible cosmetic covers are precomputed and skipped
- ✅ The same skin list now feeds the launcher's portraits, so one change fixes both
- 🟡 82 of those skins were inferred by image matching and flagged for review by their author, so they are held back at load until somebody measures them
- 🟡 A second, older look path survives in `InventoryHandler` for four items and disagrees with the new table on both the field and the value. Left alone until a capture says which is right
- ❌ **Per-character colours.** Every look is composed from the breed's default palette: there is no colour column anywhere and `customColors` is null at all eleven call sites. Two characters of the same breed and sex are tinted identically

### ⛏️ Professions

- ✅ **25,090 resources on 4,507 maps** across the six gathering jobs, with graphic → (type, skill) crossed from 305 captures
- ✅ The three states — full, depleted, busy — including the skill field moving between `f4` and `f3`
- ✅ Job levels and experience persisted, with the real curve `10 × level × (level − 1)`

- ✅ What you gather lands in the inventory, and the amount grows with job level
- ✅ Too low a job level blocks gathering the way the game does it
- ❌ Crafting professions: workshops, the craft window, and the **4,858 recipes** already in the database

### 👹 NPCs and monsters
<img width="954" height="836" alt="image" src="https://github.com/user-attachments/assets/78779a18-0cd2-4f5c-b403-0c39cd291bcb" />
<img width="2560" height="1510" alt="image" src="https://github.com/user-attachments/assets/43ebcfc4-fdbb-4924-b876-0c06743f8294" />
<img width="2558" height="1510" alt="image" src="https://github.com/user-attachments/assets/0b4adf75-9b36-4298-b428-d0444297adb3" />

- ✅ **6,468 NPC templates** with 3D looks and dialogue trees
- ✅ **422 NPCs** standing where Ankama puts them across **202 maps**, cell and orientation taken from captures, dialogue attached where it was captured
- ✅ **5,134 monsters** with native Protobuf bone models, custom scales and textures, quest monsters and archmonsters included
<img width="1700" height="930" alt="image" src="https://github.com/user-attachments/assets/02254e58-ec87-4839-82ac-f142ec5ef9cd" />

- ✅ **38,744 mapped mob groups**, respawned and kept populated, 1 to 8 monsters each
- ✅ Sub-area aware spawning across **562 sub-areas**, with radius-2 cell validation so nothing spawns on decorations or zaap pillars
- ✅ **No monsters indoors, and none standing on a zaap** — not in houses, banks or shops. The rule is two lists and one exception, and the exception is the one that matters: 753 of the 763 dungeon rooms are themselves marked indoors, so a blanket ban would empty every dungeon. 7,214 groups of 38,744 kept out, and the 763 rooms untouched
- ✅ **NPC colours**, read as what they are: `index=value` pairs, sometimes hexadecimal. The **2,045 NPCs that carry colours** render with theirs
- ✅ A dialogue always offers at least one real reply, so it can always be closed. With an empty list the client draws its own *Leave* which never answers back
- 🟡 **401 monsters have no spells at all** in the database
- ✅ **Dialogue trees.** The client holds every line an NPC can say and every reply it can be given, and never which goes with which — measured across all 6,467 NPCs, there is no field for it. That mapping has always been the server's own, so it has to be authored, and now it can be
<img width="1138" height="694" alt="image" src="https://github.com/user-attachments/assets/fc1182c3-a261-4bcd-9532-84a2ceda8dc8" />
<img width="1082" height="692" alt="image" src="https://github.com/user-attachments/assets/39cdf857-b506-4968-b2f9-c0c5f80b64c3" />

- ✅ **Monster groups placed by hand**, and Ankama's own removable, without touching the 240 MB database that gets regenerated

### 📜 Quests

<img width="2560" height="1498" alt="image" src="https://github.com/user-attachments/assets/6dbe2000-4f3c-4b41-9409-5be932f84d6e" />
<img width="1452" height="1226" alt="image" src="https://github.com/user-attachments/assets/77256193-a9dd-48df-9d0a-5408613fef34" />

**1,976 quests**, with their 2,225 steps and 15,547 objectives, read out of six Unity dumps the
repository does not even carry.

- ✅ A quest is handed over by an NPC saying a particular line — 1,260 steps declare one and every
  one of them resolves to real text, which is what ties the quest catalogue to the dialogue trees
- ✅ Objectives complete two ways: the client says so for the **5,670** that ask you to click
  something the server never sees, and the server counts for itself the ones that ask you to beat a
  monster
- ✅ Progress is written the moment it changes — there is no autosave here, and losing an evening's
  quest is worse than losing a few kamas
- 🟡 The start condition is a language of its own: **29 operators**, brackets three deep, and a `!`
  that means "not" without an `=` after it. Six operators are understood, covering every term of
  **935 of the 1,976** conditions; the rest are let through **and named**, because refusing what
  this emulator cannot model would put 53% of the game's quests out of everybody's reach

Full workings in **`docs/quests.md`**.

### 🏰 Dungeons
<img width="2550" height="1498" alt="image" src="https://github.com/user-attachments/assets/f79f7881-c68e-45b5-ae29-b4aaba928a1d" />
<img width="2560" height="1510" alt="image" src="https://github.com/user-attachments/assets/3c73a696-1de3-46ae-bea6-149bc06009fb" />
<img width="2560" height="1508" alt="image" src="https://github.com/user-attachments/assets/7b481a47-2fea-43da-a8a0-5b2692030473" />
<img width="2560" height="1510" alt="image" src="https://github.com/user-attachments/assets/a6347069-3d31-45bc-b40f-f4d942674e48" />

**187 dungeons**, with their **763 rooms**, their key and their boss.

- ✅ Talk to the guardian, hand over the key, and you are in the first room; win a fight and you
  move on; beat the boss in the last one and you come out
- ✅ The boss is placed at startup in **126** dungeons, in the room the data says, at the highest
  grade it has
- ✅ The keyring and the required item come straight from the client's own data, which is what
  makes a locked door possible
- ✅ Dungeon challenges are imposed at 0% and carry achievements

> It is not Ankama's dungeon, and the difference is worth stating: theirs is a chain of rooms and
> corridors walked through ordinary doors, and **not one of the 187 has a single one of its internal
> passages** — not in the extracted table, not in Ankama's own world graph. A player put in room 0
> would have no way out, so winning moves you instead.

Full workings in **`docs/dungeons.md`**.

### 🪙 Jondo Coin

A currency of this server's own — a real item with its own template.
<img width="1676" height="1102" alt="image" src="https://github.com/user-attachments/assets/aee2eb3f-b2a3-4c35-a35c-fafe69669355" />

- ✅ Drops from every monster at 100%, one coin per 25 monster levels: 1 for 1-25, 2 for 26-50, up to 9 at 201+
- ✅ Its own description in the five client languages, picked at runtime from the language the client is running in
- ✅ Vendors that charge in coins instead of kamas, one per category, appearance shops among them, priced by item type and rarity

See `docs/jondo-coin.md`.

---

## ⚔️ One engine, three rulebooks

There is one fight engine, and it answers three different games. It does not ask *what kind of
fight am I*; it asks **what do I do**, and the answer comes from a rules object — so adding something
to the Koliseo touches one class instead of five methods:

| | Against monsters | Duel | Koliseo |
|---|:---:|:---:|:---:|
| Challenges offered | yes | no | no |
| Placement clock | 45.0 s | — | 59.2 s |
| `kam` type | 4 | 0 | 7 |
| `kaa` countdown | yes | no | yes |
| Monster loot and experience | yes | no | no |
| Koliseo payout | no | no | yes |
| Clears the group on a win | yes | no | no |
| Moves to the next room | yes | no | no |

None of those numbers is chosen: the 4, the 0 and the 7 are the `kam`'s field 2 in the captures,
and the 592 is the `kaa`'s field 5 in the Koliseo one.

Two rules hold the rest of it together:

* **The teams are `Azul` and `Rojo`, not `Team0` and `Team1`.** Nothing assumes one side is the
  players and the other the monsters, because in a duel both sides are people.
* **Everything sent to a client is composed inside that client's own session.** Each fighter's look,
  level, characteristics and equipment come from their own record, so what the second player is sent
  describes the second player.

**Three architecture tests enforce it**, each verified by injecting a real violation and watching it
go red: no lookups that assume one team is the players, no rules decided by fight type outside the
rules object, and nothing writing to a single socket unless it is painting one person's own view.

### 🐉 PvM combat
<img width="2560" height="1510" alt="image" src="https://github.com/user-attachments/assets/d5fdf2d1-0244-4529-b2b9-06cf3dcdc1e5" />

- ✅ Tactical arenas resolved from each roleplay map by zone offset, with clean context transitions
- ✅ Placement phase with red and blue tiles and cell swapping before *Ready*
- ✅ Isometric geometry (`MapGeometry`) over a pre-computed O(1) BFS distance matrix, with no diagonal steps
- ✅ Line of sight traced between cell centres against the arena's own blocker set
- ✅ Turn protocol, 30-second timers with automatic pass, AP/MP replenishment
- ✅ Movement with per-tile MP cost and collision against occupied cells
- ✅ Loot, victory and defeat screens, experience over **1,889 levels**, level-ups and group respawn
- ✅ **End-of-fight statistics** — damage dealt by source (own casts, glyphs and walls, summons, turn triggers, pushes), taken, heals given and received, shields, enemies defeated, and the per-turn and per-AP averages, each field measured against the frames of its own fight over 30 fights; every player gets his own numbers and nobody else's, and the results list carries people and monsters, not summons
- ✅ Monster AI: a target chosen **per spell**, range measured against that target rather than against the nearest enemy, walking to the spell's own range band, `MaxCastPerTurn` honoured, breadth-first pathing around obstacles and line of sight. Measured over the 5,134 monsters: **15.1%** cannot reach the player, against 24.9% without it, and **87.2%** of action points get spent, against 58.7%
- 🟡 Weapon strikes apply damage and AP cost; the slash animation does not
- 🟡 `MaxCastPerTarget`, minimum cast interval and cast-in-line are enforced for the player, not for monsters
- ✅ **Push and collision damage**, `blockedCells × (level/2 + push − resistance + 32) / 4`, floored — measured over 127 collisions, with the resistance subtracted *inside* the quarter. The fighter acting as the wall takes half, and the **Unmovable** state cancels it. Twelve samples are locked into a startup guard
- ✅ A dropped client does not stop the fight, and the player can come back into it — see
  [Connection and authentication](#-connection-and-authentication)
- ❌ AP/MP dodge rolls, lock and tackle in melee — an illusion can tackle on the class sheet, but
  nobody tackles on this server yet

### 🤺 Duels
<img width="2560" height="1506" alt="image" src="https://github.com/user-attachments/assets/286367e0-6342-4aef-b07b-52d3bdbdf9d4" />
<img width="2560" height="1502" alt="image" src="https://github.com/user-attachments/assets/c1f3f058-81ea-4189-8f4b-312e3209f63a" />

Player against player, on the map, by challenging somebody standing there.

- ✅ Offer, accept and refuse, with the challenge id echoed through every frame of the fight
- ✅ Both fighters composed from their **own** character record — look, level, characteristics, equipment
- ✅ Placement with no clock, and no challenges offered: there are no monsters to set them against
- ✅ Victory **and defeat** screens, each player's own, and both sides returned to the map
- ✅ Nothing is won and nothing is lost — no experience, no kamas, no loot
- ✅ The end-of-fight card shows the other player's portrait instead of a question mark: an entry
  with no level is a *monster* to the client, so a person always carries theirs

### 🏟️ Koliseo
<img width="2560" height="1504" alt="image" src="https://github.com/user-attachments/assets/2b19a035-4124-41d6-9c43-0c6881f69e40" />
<img width="2560" height="1498" alt="image" src="https://github.com/user-attachments/assets/417c9bf9-c895-4a7a-8de3-ef394ce5392c" />
<img width="2560" height="1498" alt="image" src="https://github.com/user-attachments/assets/0a3bc3d6-ce61-4729-aa23-958a024c07fb" />
<img width="2560" height="1496" alt="image" src="https://github.com/user-attachments/assets/dfb0aeb1-b507-48b0-8e05-286c3fa1945e" />

Ranked PvP through a queue. Open the window, pick a format, get matched, fight, get paid.

- ✅ **The format table** (`lux` → `ltd`) — 1v1, 2v2, 3v3 open and a fourth closed, byte for byte as the capture
- ✅ **Enrolling** (`lsm`), with the format carried as the client's own enum
- ✅ **The queue state** (`lsx`) pushed back, which is what paints *searching* in the window
- ✅ **Matchmaking on enrolment**, one queue per format, drawn under a lock so two simultaneous requests cannot take the same person into two fights
- ✅ Everybody re-checked as still connected **before** anyone loses their place in the queue; if somebody dropped, the rest go back to the queue rather than pay for it
- ✅ The fight itself, with the Koliseo rulebook, and both sides returned to roleplay at the end
- ✅ **The winner is paid** — kamas, Kolichas (item 12736), Vitorichas (34478) and experience. The loser gets nothing, and its experience block carries the gained field *absent* rather than zero, which is how the capture has it
- 🟡 **The amounts are constants, not a formula.** Two winners in one capture is not enough to derive one — they go the wrong way round, the higher level earning fewer kamas — so kamas, Kolichas and Vitorichas sit in three named fields. Experience does better: over the band of the winner's own level the two samples land at 7.22% and 6.12%, so 6.67% is used
- 🚧 The *match found* popup with accept and refuse
- 🚧 Fights are held on an ordinary arena; the real game picks one of the many Koliseo maps at random
- ❌ Rankings (`iqt`, `irc`), two undeciphered lists of over three thousand bytes each
- ❌ The `lst` redirect to a separate Koliseo server. Jondo is one server and holds the fight in place

### ✨ Spell effect engine

One engine for all eighteen classes, driven entirely by client data. Not a single spell is written
by hand: everything comes out of `SpellLevels.EffectsJson` and the `Effects` catalogue, and each
thing a spell can do — push, shield, carry, summon, copy — is one primitive that every spell using
it shares. Of the **179 effects the 836 class spells use, 71 have a branch in the engine, 69 need
no code at all** — they are characteristics, read straight off the client's own `Effects` table —
**and 39 are missing**. The table, effect by effect with how many spells each touches, is
[`docs/effect-coverage.txt`](docs/effect-coverage.txt).

- ✅ Effects, triggers and target masks read from the spell — `I` on cast, `TB` turn start, `TE` turn end, `DBE` when hit, `X` on death, `CCMPARR` per tile walked; `a` allies, `A` enemies, `g` summons, `P`/`p` the caster's own summons (through its master when the caster is itself a summon), `h` the caster's summoner, `i` summons of either side, `E<n>`/`e<n>` gated on a state, `V<n>`/`v<n>` on a life threshold, `*E<n>`/`*e<n>` conditions on the caster. All judged on one snapshot taken before the cast, so a pick-up and a throw inside one spell do not see each other
- ✅ States need no code — effect 950 sets a number, 951 clears it, the masks do the rest
- ✅ Area shapes from `zoneDescr` — point, circle, cross, line, half-line, diamond, ring, square, half-circle, segment, whole map — with the inner edge (`param2`) honoured and each spell's own per-tile falloff
- ✅ Displacement — push, pull, step back, step forward, push without damage, and push or pull **to the aimed cell** (783/1043); direction taken from the centre of the area, stopping at walls, holes and fighters. Every displacement travels as a 5 (553 of 553 samples), a teleport as a 4 (581), a swap as one 8
- ✅ Teleports — to a cell, back to the previous position, symmetrical around the caster or the target — and position swaps
- ✅ **Carry and throw** (50/51) — the Pandawa's Karcham and Chamrak and the Tymobot's Pinzas are the same two primitives. The one carried leaves the board's cells and follows; the states 3 and 8 come off the spell's own cast conditions
- ✅ **Illusions** (1097) — Tymadura, byte for byte against its capture: the caster jumps to the aimed cell and copies with his stats of the moment appear two steps down each free axis of the cell he left. They stand where the aimed vector points when turned a quarter, a half and three quarters around the cell he left, hold a cell, do not play, do not sit in the carousel, and go at the first hit that deals damage — all of them when the original is hit, or at his next turn. His own side gets the captured block and sees him translucent among opaque copies; the other side gets the copies dressed as him, name and life included, and no visibility switch at all
- ✅ Criticals rolled against the spell's probability plus the character's, using the spell's separate critical effect list
- ✅ Point steal, life steal, erosion of maximum HP and damage-taken multipliers
- ✅ Healing in all five elements, AP given back, best-element damage and life steal
- ✅ **Shields**, by caster level or by HP, announced the way the client wants them: buff row 1040 and characteristic 96, stacking
- ✅ Vitality percentages (1033/1078) go out as the sheet's vitality hole plus a flat buff, which is how Último Aliento's −50% looks on the wire
- ✅ Buff panel — icon, value, remaining rounds and dispellable flag; buffs start on their delay and expire on their round
- ✅ **Stack limits** — a spell level's `MaxStack` is honoured, so a bonus that builds up stops where the game stops it
- ✅ Cooldowns and cast limits — per turn, per target, minimum interval, initial cooldown; a spell that needs an empty cell, or a taken one, is refused before the AP go
- ✅ **Nine sub-cast families, one table** — 792 is cast by the target at its own cell, 1160 by the caster at the candidate's, 1017 back at the parent caster, 2160 at the nearest eligible target under a budget so a chain cannot loop, 2794 at the parent cell; counted over 21,307 casts. Getting the caster and the cell of a chained cast wrong is what left Mosquete, Kabúm and Último Aliento without combos
- ✅ **Glyphs, traps and runes** — 623 spells, one system: the four families share a shape and differ in when they fire, and a glyph that fires goes through the ordinary cast path, so it inherits damage, resistances and announcements for free
- ✅ Summons as real fighters — own sheet, behaviour spell, lifetime, and they all fall when their summoner dies. **Whether one plays is bit 6 of its template's `m_flags`, and those that do are driven by their owner from his own client** — spell bar, moves and casts at their own grade — measured on the Osamodas and the Tymador. Capacity is the template's `summonCost` added up, not bodies counted: 485 of the 5,134 templates cost nothing
- ✅ **Bombs** — a summon that costs nothing against the limit, stays out of the carousel, detonates through its own explosion (1009) once per chain, is born in Combo I through its own spell and climbs a combo that comes whole out of spells 20497 and 20500 — a size of 100 plus the combo carried — and lines up into walls, two or three of a kind with one to six cells between them, charged on entry and at turn start, never by the Tymador's own bombs, and at double strength while he or a summon of his is playing. Polvo's "explode if destroyed" fires with the bomb still standing. Every number measured over the 22 Tymador captures; the +1 AP per living bomb and the chain reaction are not done
- ✅ **Class passives** — each class carries its own initial spell into every fight and the real server casts it before the first turn: *La Astucia del Tymador*, *El Alcance de Ocra*, *La Sombra de Sram*, *El Escudo de Feca*… Measured over 77 fights of 14 classes and kept in `content/fights/class_passives.json`; the client's data does not link them to a breed. Their turn triggers are what put the Tymador's turn state on him and hand every bomb of his two combos a turn — no rule of that is written by hand. The initial spells of a character's own choices go with it, matched by icon
- ✅ **Hooked spells fire on every trigger** — turn start, turn end, when hit, on death and per step walked, from their original caster; what goes off on a death goes out before the death itself, the way the Tymobot's does in its capture. And every chained cast is announced, once, before the first thing it does
- ✅ Item attitudes — the six Dofus and the trophies grant their spell through effect 1175. Whatever a turn trigger announces goes inside one sequence of the bearer's, which is how the real server sends the Tymobot's death at the end of its turn
- ✅ Appearance-changing spells — the transform replaces the root bones and keeps colours, skins, scale and pets, through combat action 149
- ✅ Script markers 3792 and 3793 do nothing, and that is measured: their value is a script id, not an effect
- ✅ The characteristic sheet in the shape the client expects: 53 entries in a fixed order, and a single-characteristic refresh **replaces** its entry rather than adding to it
- ❌ A cooldown pinned to a number of turns (1045), a spell's own basic-healing bonus (2935), damage as a share of the damage taken (1223), best-element healing (3002), damage sharing and interception, portals, revealing invisibles, MP steal — the full missing list is in the coverage table
- ❌ Area shapes `G` (55 effects), `*` (10), `;` and `O`, which fall back to the centre tile alone, and the target-mask letters the check-list below names, which apply to nobody

> The engine is shared, so every class gets whatever its spells happen to use. Only the **Ocra** and the **Tymador** have been fired against the real client spell by spell, and the check-list below says which spells. A spell only works when **all** of its effects resolve, and the gaps concentrate in a handful of effect families, so they close in blocks rather than one spell at a time.

### 🔬 Spell check-list

Every class spell, one line each, in the order of the spell book. ✅ means it has been fired
against the real client, or checked against its own capture, and does what the game does. ❌ means
it has not — either the engine cannot resolve part of it on paper, and the reason follows the
dash (an effect with no implementation, a target mask or an area shape the engine does not read;
sub-casts are followed, so a gap in a chained spell shows on the spell that starts the chain), or
it resolves on paper and nobody has checked it yet, and there is no reason. A spell only counts
when **all** of it works: a spell whose rebound is measured and right but whose third effect
has no implementation stays a ❌.

The list is generated from `world.db` and the engine's own source by `tools/spell_checklist.py`;
the ✅ are set by hand in it, from what has been seen. What explains most of the crosses is not
an effect but the target-mask letters the engine does not read yet (`c`, `O`, `J`, `M`, `T`,
`L`, `j`, `R`, `U`, `l`, `m`, `r`, `b<n>`, `PB`/`pb`), then the area shape `G`, and only then
the effects: a cooldown pinned to a number of turns (1045), a spell's own basic-healing bonus
(2935), maximised random rolls (782), damage as a share of the damage taken (1223).

| Class | Seen working | Resolve on paper | Spells |
|---|:---:|:---:|:---:|
| Feca | 0 | 26 | 44 |
| Osamodas | 0 | 29 | 44 |
| Anutrof | 0 | 34 | 44 |
| Sram | 0 | 35 | 44 |
| Xelor | 0 | 10 | 44 |
| Zurcarák | 0 | 6 | 44 |
| Aniripsa | 0 | 8 | 44 |
| Yopuka | 0 | 36 | 44 |
| Ocra | 5 | 28 | 44 |
| Sadida | 0 | 28 | 44 |
| Sacrógrito | 0 | 33 | 44 |
| Pandawa | 0 | 29 | 44 |
| Tymador | 10 | 34 | 44 |
| Zobal | 0 | 29 | 44 |
| Steamer | 0 | 23 | 44 |
| Selatrop | 0 | 11 | 44 |
| Hipermago | 0 | 8 | 44 |
| Uginak | 0 | 3 | 44 |
| Forjalanza | 0 | 9 | 44 |
| **All** | **15** | **419** | **836** |

<details><summary><b>Feca</b> — 0 of 44 seen working, 26 resolve on paper</summary>

- ❌ Somnolencia
- ❌ Maniobra
- ❌ Languidez
- ❌ Atonía
- ❌ Murallón
- ❌ Fortificación
- ❌ Tifón
- ❌ Borrasca
- ❌ Escalofrío
- ❌ Chaparrón
- ❌ Barricada
- ❌ Pavés — mask `U`
- ❌ Recelo — effect 202 (reveals invisible entities), effect 402 (places an end-of-turn glyph), mask `U`
- ❌ Parapeto — effect 1165 (places to glyph), effect 2018 (dispels glyphs), effect 202 (reveals invisible entities), mask `U`, shape `O`
- ❌ Letargo
- ❌ Reagrupamiento
- ❌ Nimbo
- ❌ Estrato — shape `G`
- ❌ Bastión
- ❌ Tregua
- ❌ Puñalada
- ❌ Tetania
- ❌ Pompa
- ❌ Cencerro
- ❌ Silbo
- ❌ Cayado
- ❌ Sopor
- ❌ Escapadita
- ❌ Aprisco — shape `G`
- ❌ Excursión — effect 1165 (places to glyph)
- ❌ Escudo feca — mask `b1`
- ❌ Posición Defensiva — effect 1026 (triggers glyphs), mask `b1`
- ❌ Refuerzo
- ❌ Ataraxia
- ❌ Prado — effect 202 (reveals invisible entities), shape `*`
- ❌ Pasto — effect 2018 (dispels glyphs), effect 202 (reveals invisible entities)
- ❌ Valle — effect 202 (reveals invisible entities)
- ❌ Escarcha — effect 2018 (dispels glyphs), effect 202 (reveals invisible entities), shape `G`
- ❌ Tierra Batida — effect 202 (reveals invisible entities)
- ❌ Refugio — effect 2018 (dispels glyphs), effect 202 (reveals invisible entities)
- ❌ Trashumancia — effect 1026 (triggers glyphs), mask `b1`
- ❌ Égida — effect 765 (intercepts damage), mask `U`, mask `b1`
- ❌ Tierra Quemada — effect 202 (reveals invisible entities), shape `G`
- ❌ Vigía — effect 2018 (dispels glyphs), effect 202 (reveals invisible entities)

</details>

<details><summary><b>Osamodas</b> — 0 of 44 seen working, 29 resolve on paper</summary>

- ❌ Grito de Cuerbok
- ❌ Colmillos de Milubo
- ❌ Pinchos de Prespic
- ❌ Desplume
- ❌ Dientes de Piranya
- ❌ Corazón Salvaje
- ❌ Grito del Oso — shape `G`
- ❌ Baba de Sapo — shape `G`
- ❌ Látigo
- ❌ Fusta
- ❌ Tofu
- ❌ Garras de Chtigre
- ❌ Jalató
- ❌ Garras de Buitre — effect 786 (heals the attacker for #1% of the damage), shape `F`
- ❌ Saponito
- ❌ Vellocino de Oro
- ❌ Dragún
- ❌ Mordedura de Serpiente
- ❌ Séquito Salvaje — effect 285 (#1: -#3 AP), mask `j`
- ❌ Pacto Bestial — effect 1045 (#1: cooldown pinned to #3 turns), mask `j`, mask `u`
- ❌ Chute Motivador — mask `J`, mask `L`, mask `M`, mask `c`, mask `j`, mask `l`, mask `m`
- ❌ Comunión Animal — effect 1061 (shares damage), mask `c`, mask `j`
- ❌ Salta la Ranadina — mask `J`, mask `j`
- ❌ Tornado de Plumas
- ❌ Soplido Dracónico
- ❌ Golpe del Crujidor
- ❌ Carga Bestial — mask `J`, mask `j`
- ❌ Canto de Fénix
- ❌ Golpazo Aéreo — mask `J`, mask `j`
- ❌ Torbellino
- ❌ Disciplina
- ❌ Fuete — mask `j`
- ❌ Gorditofu
- ❌ Crujintesco
- ❌ Jalatorpe
- ❌ Cocolérico
- ❌ Saponcio
- ❌ Azufrénix
- ❌ Dragonito
- ❌ Escararrayo
- ❌ Lazo Espiritual — effect 2184 (Sigue to the lanzador), mask `c`
- ❌ Relevo Espiritual — mask `c`, mask `j`
- ❌ Espíritu Glotón — effect 1045 (#1: cooldown pinned to #3 turns)
- ❌ Espíritu Burlón — effect 1045 (#1: cooldown pinned to #3 turns)

</details>

<details><summary><b>Anutrof</b> — 0 of 44 seen working, 34 resolve on paper</summary>

- ❌ Lanzamiento de Monedas
- ❌ Moneda sonante
- ❌ Pala Fantomática
- ❌ Último Recurso
- ❌ Mochila Animada — effect 765 (intercepts damage)
- ❌ Morral Animado — effect 1061 (shares damage)
- ❌ Jarabe de Pala
- ❌ Desprendimiento
- ❌ Bancarrota — mask `J`, mask `L`, mask `M`, mask `j`, mask `l`, mask `m`
- ❌ Lanzamiento de Pala
- ❌ Fiebre del Oro
- ❌ Andador
- ❌ Caja de Pandora
- ❌ Caja de Herramientas — mask `j`, mask `l`, mask `u`
- ❌ Fuerza de la Edad
- ❌ Búsqueda de Oro
- ❌ Llave del Tesoro
- ❌ Llave de Brazo
- ❌ Subterráneo
- ❌ Laya de los Ancianos
- ❌ Palas animadas
- ❌ Laya Animada
- ❌ Avaricia
- ❌ Decadencia
- ❌ Pala Aurífera
- ❌ Turbera
- ❌ Torpeza
- ❌ Edad de Oro
- ❌ Terraplenado — mask `D`, mask `H`, mask `J`, mask `M`, mask `d`, mask `j`, mask `m`
- ❌ Fuego de Mina — effect 786 (heals the attacker for #1% of the damage)
- ❌ Oportunidad
- ❌ Explosión de Grisú — mask `c`
- ❌ Debilitación
- ❌ Obsolescencia — mask `J`, mask `L`, mask `M`, mask `j`, mask `l`, mask `m`
- ❌ Jubilación Anticipada
- ❌ Pala de la Fortuna
- ❌ Corrupción
- ❌ Túnel de Fortuna
- ❌ Caducidad — mask `J`, mask `j`
- ❌ Tamizado — shape `G`
- ❌ Pala de los Ancianos
- ❌ Filón
- ❌ Cofre Animado
- ❌ Arcón Animado

</details>

<details><summary><b>Sram</b> — 0 of 44 seen working, 35 resolve on paper</summary>

- ❌ Invisibilidad
- ❌ Bruma
- ❌ Trampas solapadas
- ❌ Zalagarda — effect 786 (heals the attacker for #1% of the damage)
- ❌ Truhanería
- ❌ Abrojo
- ❌ Arsénico
- ❌ Toxinas
- ❌ Trampa Repulsiva
- ❌ Trampa Espeluznante
- ❌ Engaño
- ❌ Rebanacuellos
- ❌ Doble — effect 180 (summons to double of the caster), effect 2027 (Toma el control de la entidad), mask `D`, mask `H`, mask `I`, mask `M`, mask `U`
- ❌ Conspirador — effect 180 (summons to double of the caster), effect 2027 (Toma el control de la entidad), mask `D`, mask `H`, mask `I`, mask `M`, mask `U`
- ❌ Trampa Fangosa
- ❌ Epidemia — mask `D`, mask `H`, mask `I`, mask `M`, mask `c`
- ❌ Extorsión
- ❌ Registro
- ❌ Crueldad
- ❌ Mala Sombra — effect 2973 (heals #1-#2% of the damage dealt), effect 781 (minimises the target random rolls)
- ❌ Trampa Funesta
- ❌ Efracción
- ❌ Trampa de Inmovilización
- ❌ Fosa Común
- ❌ Trampa Miserable
- ❌ Trampa de Fragmentación
- ❌ Estafa
- ❌ Hurto
- ❌ Pillaje
- ❌ Ataque Mortal
- ❌ Miedo
- ❌ Equivocación
- ❌ Karadura
- ❌ Perfidia
- ❌ Concentración de Chakra
- ❌ Artimaña
- ❌ Trampas mortales
- ❌ Calamidad — shape `G`
- ❌ Escapatoria — mask `O`
- ❌ Marca Mortuoria
- ❌ Trampa de Deriva
- ❌ Trampa Insidiosa
- ❌ Estratagema — shape `O`
- ❌ Inyección Tóxica — effect 1036 (#1: -#3 cooldown)

</details>

<details><summary><b>Xelor</b> — 0 of 44 seen working, 10 resolve on paper</summary>

- ❌ Teletransportación — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`
- ❌ Astrolabio — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`, mask `c`
- ❌ Perturbación — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`
- ❌ Rueda Dentada — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`, shape `O`
- ❌ Recuerdo — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`
- ❌ Permutación — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`
- ❌ Marchitación
- ❌ Aguja — effect 1406 (removes the effects of grade #1 of spell #2)
- ❌ Rebobinamiento — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1099 (teleports to the turn-start position), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`, mask `c`
- ❌ Remanencia — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`, mask `c`
- ❌ Refracción — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`
- ❌ Regulador — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), effect 290 (#1: +#3 cast(s) per turn), mask `O`, mask `T`
- ❌ Cómplice
- ❌ Esfera de Xelor
- ❌ Congelación — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`
- ❌ Polvo — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`
- ❌ Ralentización
- ❌ Reloj de Arena de Xelor — effect 1406 (removes the effects of grade #1 of spell #2)
- ❌ Engranaje — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`
- ❌ Cuentagotas — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`
- ❌ Borroso Temporal
- ❌ Conservación
- ❌ Distorsión — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`, shape `G`
- ❌ Arenas del Tiempo — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`
- ❌ El Tiempo Vuela — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`
- ❌ Premonición — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1101 (teleports o intercambia posiciones), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`
- ❌ Rayo Oscuro — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`
- ❌ Desecamiento — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`
- ❌ Paradoja — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`
- ❌ Falla — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`, mask `u`
- ❌ Syncro
- ❌ Tañido — shape `G`
- ❌ Petrificación — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), effect 285 (#1: -#3 AP), mask `O`, mask `T`
- ❌ Reloj de Bolsillo — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), effect 2018 (dispels glyphs), mask `O`, mask `T`
- ❌ Reloj — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`
- ❌ Reloj de Agua — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`
- ❌ Golpe de Xelor — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`
- ❌ Péndulo — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`
- ❌ Momificación — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`
- ❌ 25ª Hora
- ❌ Rolbac — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`
- ❌ Inestabilidad
- ❌ Desincronización
- ❌ Espaciotiempo — effect 1026 (triggers glyphs), effect 1045 (#1: cooldown pinned to #3 turns), effect 1223 (damage: #1-#2% of the final damage taken), mask `O`, mask `T`

</details>

<details><summary><b>Zurcarák</b> — 0 of 44 seen working, 6 resolve on paper</summary>

- ❌ Espíritu Felino — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Kraps — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Garra Invocadora
- ❌ Caricia Invocadora
- ❌ Golpe de Fortuna — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Redistribución — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Olfato — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Rueda de la Fortuna — effect 2935 (#1: +#3 basic healing on that spell), effect 781 (minimises the target random rolls), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Reflejos — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Lametazo — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Truco — effect 285 (#1: -#3 AP), effect 287 (#1: +#3% de crítico), effect 2935 (#1: +#3 basic healing on that spell), effect 296 (#1: +#3 AP), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Todo o Nada — effect 2935 (#1: +#3 basic healing on that spell), effect 3002 (#1-#2 best-element healing), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Salto del Felino
- ❌ Trenzado
- ❌ Topkaj — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Garra Juguetona — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Jass — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Desdicha — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Cara o Cruz — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Fantasmada — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Segunda Oportunidad — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Nueve Vidas — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Farol — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Rekop
- ❌ Almohadillas — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Bufido — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Yams — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Lengua Raspadora — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Belote — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Peligro — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Baraka — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Osadía — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Ruleta
- ❌ Tarot de Zurcarák — effect 1045 (#1: cooldown pinned to #3 turns), effect 1099 (teleports to the turn-start position), effect 285 (#1: -#3 AP), effect 290 (#1: +#3 cast(s) per turn), effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Castillo de Naipes — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Buena Estrella — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Blakjak — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Destino de Zurcarák — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Percepción — effect 202 (reveals invisible entities)
- ❌ Predación — effect 202 (reveals invisible entities)
- ❌ Ovillo — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Garra de Ceangal — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Feliación — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`
- ❌ Desventura — effect 2935 (#1: +#3 basic healing on that spell), effect 782 (Maximiza los efectos aleatorios en el objetivo), mask `c`, shape `O`

</details>

<details><summary><b>Aniripsa</b> — 0 of 44 seen working, 8 resolve on paper</summary>

- ❌ Palabra de Amistad — effect 1045 (#1: cooldown pinned to #3 turns), effect 402 (places an end-of-turn glyph)
- ❌ Palabra Alquímica — effect 290 (#1: +#3 cast(s) per turn), mask `x`
- ❌ Palabra Escandalosa — mask `O`
- ❌ Grito Ensordecedor — mask `O`
- ❌ Palabra Juguetona — mask `O`
- ❌ Palabra Maliciosa — mask `O`
- ❌ Palabra Vampírica — mask `O`
- ❌ Sollozos — effect 2973 (heals #1-#2% of the damage dealt), mask `O`
- ❌ Palabra Estimulante
- ❌ Palabra de Declive — effect 2935 (#1: +#3 basic healing on that spell)
- ❌ Blasfemia — mask `O`
- ❌ Ungüento Ancestral — mask `O`
- ❌ Pintura de Guerra — mask `O`
- ❌ Palabra Secreta — effect 2935 (#1: +#3 basic healing on that spell), mask `O`
- ❌ Lamentos — effect 2973 (heals #1-#2% of the damage dealt), mask `O`
- ❌ Demencia — mask `O`
- ❌ Palabra Turbulenta — mask `O`
- ❌ Palabra Furiosa — mask `O`
- ❌ Palabra Revitalizante
- ❌ Palabra Galvanizadora
- ❌ Palabra Bromista — mask `O`
- ❌ Palabra Censurada — mask `O`
- ❌ Palabra Florida — mask `O`, shape `*`
- ❌ Bosquecillo Encantado — mask `O`
- ❌ Palabra de Juventud
- ❌ Palabra Deprimente — effect 2935 (#1: +#3 basic healing on that spell)
- ❌ Grito de Guerra — mask `O`
- ❌ Palabra Ritual — mask `O`, shape `O`
- ❌ Palabra Prohibida — mask `O`
- ❌ Palabra Exangüe — effect 786 (heals the attacker for #1% of the damage), mask `O`
- ❌ Palabra Abrumadora — effect 2935 (#1: +#3 basic healing on that spell)
- ❌ Palabra Desanimadora — effect 2935 (#1: +#3 basic healing on that spell)
- ❌ Ladronceo — effect 320 (steals #1-#2 range), mask `O`
- ❌ Palabra Entretenida — mask `O`
- ❌ Palabra de Vuelo
- ❌ Fuente de Juventud — effect 402 (places an end-of-turn glyph), mask `c`
- ❌ Pincel Tribal — mask `O`, mask `c`
- ❌ Coro Estridente — effect 2935 (#1: +#3 basic healing on that spell), mask `O`
- ❌ Crioterapia — mask `O`
- ❌ Murmullo — effect 77 (Roba #1-#2 MP), mask `O`
- ❌ Palabra de Pavor
- ❌ Escalpelo — effect 3002 (#1-#2 best-element healing)
- ❌ Palabra de Reconstitución
- ❌ Palabra de Solidaridad

</details>

<details><summary><b>Yopuka</b> — 0 of 44 seen working, 36 resolve on paper</summary>

- ❌ Machete
- ❌ Acumulación — mask `c`
- ❌ Intimidación
- ❌ Conquista
- ❌ Salto
- ❌ Agitación
- ❌ Fervor
- ❌ Amenaza
- ❌ Espada Divina
- ❌ Espada del Juicio
- ❌ Espada Destructora
- ❌ Fustigación
- ❌ Aguante
- ❌ Pugilato
- ❌ Soplido
- ❌ Congregación
- ❌ Concentración — mask `J`, mask `L`, mask `M`, mask `c`, mask `j`, mask `l`, mask `m`
- ❌ Sentencia
- ❌ Furor
- ❌ Ira de Yopuka
- ❌ Fricción — mask `O`
- ❌ Golpe por Golpe — mask `O`
- ❌ Influencia
- ❌ Duelo Yopukil
- ❌ Potencia
- ❌ Vindicta
- ❌ Virtud
- ❌ Masacre — effect 1223 (damage: #1-#2% of the final damage taken)
- ❌ Tempestad de Potencia
- ❌ Casca
- ❌ Espada Celeste
- ❌ Cénit — effect 1013 (#1-#2 air damage (% MP restantes))
- ❌ Vitalidad — mask `c`
- ❌ Violencia
- ❌ Espada de Yopuka
- ❌ Cuchillo de Carnicero
- ❌ Espada del Destino
- ❌ Tumulto
- ❌ Presión
- ❌ Fractura
- ❌ Oleada
- ❌ Anillo Destructor — shape `O`
- ❌ Precipitación
- ❌ Determinación

</details>

<details><summary><b>Ocra</b> — 5 of 44 seen working, 28 resolve on paper</summary>

- ✅ Flecha Helada — the critical roll, measured
- ❌ Flecha Acosante
- ❌ Flecha de Pelea
- ❌ Diamantes Destructores — shape `F`
- ❌ Flecha Azotadora
- ❌ Flecha Asaltante — mask `c`
- ❌ Flecha Vagabunda
- ❌ Flecha Evasiva — mask `c`
- ❌ Paso de Cacería
- ❌ Baliza Táctica — mask `O`, mask `U`
- ❌ Disparos Lejanos — mask `b9`
- ❌ Tiro Penetrante
- ❌ Flecha Detonadora
- ❌ Flecha Ralentizante
- ❌ Flecha de Abolición — mask `PB`, mask `pb`
- ❌ Flecha Perseguidora
- ❌ Flecha de Retroceso
- ❌ Flecha Impactante
- ❌ Flecha Inmovilizadora — effect 77 (Roba #1-#2 MP)
- ❌ Flecha Tiránica
- ❌ Tiros Potentes
- ❌ Flechas Amorosas — effect 1061 (shares damage), mask `c`, mask `d`, mask `m`
- ❌ Flecha de Dispersión
- ❌ Flechas Flamígeras
- ❌ Flecha Explosiva
- ❌ Flecha Masacrante
- ❌ Ojo de Topo — effect 202 (reveals invisible entities)
- ❌ Lluvia de Flechas
- ❌ Ojo por Ojo
- ❌ Flecha Paralizadora — shape `G`
- ❌ Baliza de Supervivencia — mask `U`
- ✅ Represalias
- ✅ Tiro de Repliegue
- ❌ Vendetta
- ✅ Flecha Castigadora — its start, measured
- ❌ Flecha del Juicio — effect 1016 (#1-#2 earth damage (% MP restantes))
- ❌ Flecha de Expiación
- ❌ Flecha de Redención — effect 1406 (removes the effects of grade #1 of spell #2)
- ❌ Flecha Percutiente — mask `PB`, mask `pb`
- ❌ Flecha Búmeran
- ❌ Flecha Voraz
- ✅ Flecha Fulminante — the rebound, measured from both sides
- ❌ Agudeza Absoluta — effect 289 (#1: line of sight disabled)
- ❌ Centinela — effect 202 (reveals invisible entities)

</details>

<details><summary><b>Sadida</b> — 0 of 44 seen working, 28 resolve on paper</summary>

- ❌ La Loca
- ❌ La Loca Transmutada
- ❌ Árbol — mask `U`, mask `s`
- ❌ Árbol Frondoso — mask `U`, mask `s`
- ❌ Zarza
- ❌ Zarza Insolente
- ❌ Plaga — shape `G`
- ❌ Bosque Encantado — shape `G`
- ❌ La Bloqueadora
- ❌ La Bloqueadora Transmutada
- ❌ Lágrima de Sadida — effect 786 (heals the attacker for #1% of the damage), mask `l`
- ❌ Subida de Savia — effect 2973 (heals #1-#2% of the damage dealt), effect 786 (heals the attacker for #1% of the damage), mask `U`, mask `l`, mask `s`, shape `G`
- ❌ Savia Paralizante
- ❌ Miasmas
- ❌ Zarza Tranquilizadora — effect 3002 (#1-#2 best-element healing)
- ❌ Trasplante
- ❌ Potencia Silvestre — effect 2796 (kills the target and replaces it with summon: #1)
- ❌ Influencia Vegetal — effect 2796 (kills the target and replaces it with summon: #1)
- ❌ La Sacrificada
- ❌ La Sacrificada Transmutada
- ❌ Temblor — effect 202 (reveals invisible entities), mask `c`
- ❌ Mandrágora
- ❌ Don Natural — effect 1061 (shares damage), effect 3002 (#1-#2 best-element healing), mask `c`, mask `d`, mask `m`
- ❌ Armonía — effect 1061 (shares damage)
- ❌ Sacrificio Vudú — effect 1045 (#1: cooldown pinned to #3 turns), mask `J`, mask `L`, mask `M`, mask `j`, mask `l`, mask `m`
- ❌ Cardos Ardientes
- ❌ Contagio
- ❌ Manglar — effect 786 (heals the attacker for #1% of the damage), mask `U`, mask `l`, mask `s`
- ❌ Inoculación
- ❌ Fuerza de la Naturaleza
- ❌ La Hinchable
- ❌ La Hinchable Transmutada
- ❌ Zarzas Agresivas — effect 77 (Roba #1-#2 MP)
- ❌ Fetiches Calcinados — effect 1223 (damage: #1-#2% of the final damage taken)
- ❌ Árbol de Vida
- ❌ Altruismo Vegetal
- ❌ Matorral Ardiente
- ❌ Fuego Montés
- ❌ Cicuta
- ❌ Viento Envenenado
- ❌ Hierbas Locas
- ❌ Maldición Vudú
- ❌ La Superpoderosa
- ❌ La Superpoderosa Transmutada

</details>

<details><summary><b>Sacrógrito</b> — 0 of 44 seen working, 33 resolve on paper</summary>

- ❌ Mutilación
- ❌ Pacto de Sangre
- ❌ Espada Voraz
- ❌ Espada Bailarina
- ❌ Rapapolvo
- ❌ Fulgor
- ❌ Asalto
- ❌ Aversión
- ❌ Transposición
- ❌ Fluctuación
- ❌ Condensación
- ❌ Aflujo
- ❌ Hostilidad
- ❌ Proyección
- ❌ Corona de Espinas — effect 1223 (damage: #1-#2% of the final damage taken)
- ❌ Picota
- ❌ Transfusión — effect 89 (neutral damage: #1-#2% of the caster HP)
- ❌ Lazos de Sangre
- ❌ Hecatombe
- ❌ Corte
- ❌ Baño de Sangre — shape `G`
- ❌ Inmolación
- ❌ Sacrificio — effect 765 (intercepts damage)
- ❌ Penitencia — mask `O`, mask `c`
- ❌ Desolación
- ❌ Desencadenamiento — shape `G`
- ❌ Disolución
- ❌ Carnicería
- ❌ Libación
- ❌ Castigo — effect 89 (neutral damage: #1-#2% of the caster HP)
- ❌ Berserker
- ❌ Ritual de Jashin — effect 1223 (damage: #1-#2% of the final damage taken), mask `c`
- ❌ Absorción
- ❌ Furia
- ❌ Suplicio — effect 786 (heals the attacker for #1% of the damage)
- ❌ Nerviosismo — shape `O`
- ❌ Estasis
- ❌ Escozor
- ❌ Atracción
- ❌ Perfusión — effect 2020 (heals #1-#2% of the damage taken)
- ❌ Punición
- ❌ Locura Sanguinaria
- ❌ Hemorragia
- ❌ Aniquilamiento

</details>

<details><summary><b>Pandawa</b> — 0 of 44 seen working, 29 resolve on paper</summary>

- ❌ Palma Explosiva — effect 296 (#1: +#3 AP)
- ❌ Destilación — shape `G`
- ❌ Resaca
- ❌ Soplido Flamígero
- ❌ Comilona
- ❌ Tranka
- ❌ Terror
- ❌ Consuelo
- ❌ Ventolera
- ❌ Jarana — mask `c`
- ❌ Karcham
- ❌ Chamrak
- ❌ Ola Marejadora
- ❌ Pandjiu
- ❌ Desalojo
- ❌ Soplido Alcoholizado
- ❌ Ebriedad
- ❌ Embriaguez
- ❌ Estabilización
- ❌ Escalada — effect 290 (#1: +#3 cast(s) per turn), effect 291 (#1: +#3 lanzamiento(s) per objetivo)
- ❌ Enlace Espirituoso
- ❌ Bambú
- ❌ Etilo
- ❌ Entumecimiento
- ❌ Aguardiente — mask `K`
- ❌ Aguachirle — mask `c`
- ❌ Deshonra
- ❌ Maceración
- ❌ Fermentación
- ❌ Bambusería
- ❌ Propulsión — mask `K`
- ❌ Absenta — mask `c`
- ❌ Camilla — mask `K`
- ❌ Alcoshu — mask `c`
- ❌ Pandikulación — mask `K`
- ❌ Licor — mask `c`
- ❌ Náuseas
- ❌ Cascada — mask `K`
- ❌ Leche de Bambú
- ❌ Interdicción — mask `c`
- ❌ Frasco Explosivo
- ❌ Pandatak
- ❌ Pandenkulo
- ❌ Mano de Pandawa — effect 297 (#1: casilla ocupada necesaria desactivada), effect 299 (#1: casilla libre necesaria activada)

</details>

<details><summary><b>Tymador</b> — 10 of 44 seen working, 34 resolve on paper</summary>

- ✅ Detonador
- ❌ Estopín
- ✅ Explobomba
- ❌ Explobomba Resiliente — mask `U`
- ✅ Tornabombas
- ❌ Tornabomba Resiliente — mask `U`
- ❌ Patada
- ❌ Ardid
- ❌ Extracción
- ❌ Cadencia
- ❌ Imantación — mask `b12`; does nothing on an empty cell, as it should
- ❌ Cruce — shape `*`
- ❌ Fusil — shape `-`
- ❌ Obliteración
- ❌ Jugarreta
- ❌ Bomba Ambulante — mask `U`, mask `j`
- ✅ Bombas de agua
- ❌ Bomba de Agua Resiliente — mask `U`
- ✅ Tymobot — and its own Empujoncito, Aspirador and Pinzas, driven from the owner's client; it dies at the end of its turn
- ❌ Megabomba
- ❌ Bombardeo
- ❌ Metralla
- ❌ Receptación
- ❌ Emplomado
- ✅ Tymadura — byte for byte against its capture
- ❌ Argucia
- ❌ Púlsar
- ❌ Perdigonazo
- ❌ Remisión — mask `O`
- ❌ Búnker
- ❌ Dagas Bumerán
- ❌ Tromba
- ❌ Polvo
- ❌ Bomba Pegajosa
- ✅ Kabúm
- ❌ Impostura
- ✅ Último Aliento
- ❌ Trampa Magnética
- ✅ Mosquete
- ❌ Granalla
- ❌ Colado — shape `O`
- ❌ Arcabuz
- ✅ Sismobomba
- ❌ Sismobomba Resiliente — mask `U`

</details>

<details><summary><b>Zobal</b> — 0 of 44 seen working, 29 resolve on paper</summary>

- ❌ Boliche
- ❌ Ronda — shape `O`
- ❌ Catalepsia
- ❌ Apostasía
- ❌ Máscara Eskérdikat — effect 1036 (#1: -#3 cooldown), effect 1045 (#1: cooldown pinned to #3 turns), mask `b14`
- ❌ Máscara de Cobarde — effect 1036 (#1: -#3 cooldown), effect 1045 (#1: cooldown pinned to #3 turns), mask `b14`
- ❌ Brincadeira
- ❌ Picado
- ❌ Apoyo
- ❌ Pivote
- ❌ Máscara Sáikopat — effect 1036 (#1: -#3 cooldown), effect 1045 (#1: cooldown pinned to #3 turns), mask `b14`
- ❌ Máscara de Histérico — effect 1036 (#1: -#3 cooldown), effect 1045 (#1: cooldown pinned to #3 turns), mask `b14`
- ❌ Furial
- ❌ Bocciara
- ❌ Cabriola
- ❌ Purgatorio
- ❌ Tortoruga — mask `c`, mask `j`, mask `l`, mask `m`
- ❌ Armaduro
- ❌ Esprín
- ❌ Scudo — mask `c`, mask `j`, mask `l`, mask `m`
- ❌ Apatía
- ❌ Retención
- ❌ Coraza — mask `c`, mask `j`, mask `l`, mask `m`
- ❌ Ginga — mask `c`, mask `j`, mask `l`, mask `m`
- ❌ Fogosidad
- ❌ Mascarada — effect 279 (damage neutrales: #1-#2% HP faltantes of the lanzador), effect 89 (neutral damage: #1-#2% of the caster HP), mask `c`
- ❌ Desbandada
- ❌ Comedia
- ❌ Parafuso
- ❌ Martelo
- ❌ Ponteira
- ❌ Agular
- ❌ Trance — mask `c`, mask `j`, mask `l`, mask `m`
- ❌ Neurosis
- ❌ Cabalgata
- ❌ Reclamo
- ❌ Infernus
- ❌ Distancia
- ❌ Carnavalo
- ❌ Transfiguración
- ❌ Máscara de Intrépido — effect 1036 (#1: -#3 cooldown), effect 1045 (#1: cooldown pinned to #3 turns), mask `b14`
- ❌ Máscara de Incansable — effect 1036 (#1: -#3 cooldown), effect 1045 (#1: cooldown pinned to #3 turns), mask `b14`
- ❌ Mueca — effect 1045 (#1: cooldown pinned to #3 turns), mask `l`
- ❌ Difracción — mask `J`, mask `L`, mask `M`, mask `c`, mask `j`, mask `l`, mask `m`

</details>

<details><summary><b>Steamer</b> — 0 of 44 seen working, 23 resolve on paper</summary>

- ❌ Torpedo
- ❌ Timón — shape `*`
- ❌ Catalejo
- ❌ Corrosión
- ❌ Amarre
- ❌ Ventalla — effect 1036 (#1: -#3 cooldown), effect 2027 (Toma el control de la entidad)
- ❌ Evolución — effect 2027 (Toma el control de la entidad)
- ❌ Sobretensión — effect 2027 (Toma el control de la entidad)
- ❌ Albarrama — effect 765 (intercepts damage), mask `O`
- ❌ Recursividad — effect 1023 (Intercambio de posiciones (forzado)), effect 2017 (#1)
- ❌ Escafandra
- ❌ Blindaje — mask `c`
- ❌ Anclaje
- ❌ Cortocircuito — effect 1036 (#1: -#3 cooldown), effect 2027 (Toma el control de la entidad)
- ❌ Guardianas
- ❌ Perforadora
- ❌ Corriente
- ❌ Harmatán
- ❌ Sabotaje — effect 1036 (#1: -#3 cooldown), effect 2027 (Toma el control de la entidad)
- ❌ Periscopio — effect 77 (Roba #1-#2 MP)
- ❌ Asistencia
- ❌ Derivación — effect 1023 (Intercambio de posiciones (forzado))
- ❌ Aspiración
- ❌ Pistón
- ❌ Tactiquillas
- ❌ Batiscafo
- ❌ Arponeras
- ❌ Arrastrero
- ❌ Socorrismo — effect 3002 (#1-#2 best-element healing)
- ❌ Salvamento
- ❌ Tridente — shape `F`
- ❌ Cabestrante — shape `G`
- ❌ Sónar — effect 202 (reveals invisible entities)
- ❌ Emboscada
- ❌ Compás — effect 1023 (Intercambio de posiciones (forzado)), mask `c`
- ❌ Brújula — effect 1023 (Intercambio de posiciones (forzado))
- ❌ Turbina — effect 2027 (Toma el control de la entidad)
- ❌ Piratería — shape `G`
- ❌ Buceo
- ❌ Zambullida
- ❌ Resacón
- ❌ Espuma de Mar
- ❌ Marea — effect 1045 (#1: cooldown pinned to #3 turns), effect 290 (#1: +#3 cast(s) per turn), effect 2905 (#1: alcance máximo fijado en #3), effect 2906 (#1: alcance mínimo fijado en #3), mask `c`
- ❌ Vapor — effect 2027 (Toma el control de la entidad)

</details>

<details><summary><b>Selatrop</b> — 0 of 44 seen working, 11 resolve on paper</summary>

- ❌ Portal — effect 1181 (places a portal (+#3% damage, +#1% per cell travelled))
- ❌ Errancia — effect 1181 (places a portal (+#3% damage, +#1% per cell travelled))
- ❌ Insulto — mask `r`
- ❌ Desprecio
- ❌ Audacia — mask `R`, mask `r`
- ❌ Tribulación
- ❌ Shock — mask `R`, mask `r`
- ❌ Convulsión
- ❌ Rayo de Wakfu
- ❌ Resplandor — mask `R`, mask `r`
- ❌ Neutral — effect 1183 (deactivates a portal)
- ❌ Interrupción — effect 1183 (deactivates a portal)
- ❌ Afrenta — mask `R`
- ❌ Aplomo — mask `R`
- ❌ Trascendencia — effect 290 (#1: +#3 cast(s) per turn)
- ❌ Exilio — effect 1181 (places a portal (+#3% damage, +#1% per cell travelled)), effect 1182 (Teleportal)
- ❌ Terapia
- ❌ Puño Relámpago — mask `R`, mask `r`
- ❌ Distribución — effect 2973 (heals #1-#2% of the damage dealt)
- ❌ Soberbia — effect 1223 (damage: #1-#2% of the final damage taken), mask `O`
- ❌ Estela — effect 1181 (places a portal (+#3% damage, +#1% per cell travelled))
- ❌ Estupor — effect 1181 (places a portal (+#3% damage, +#1% per cell travelled))
- ❌ Acoso — mask `R`, mask `r`
- ❌ Cataclismo — effect 2973 (heals #1-#2% of the damage dealt), shape `G`
- ❌ Sanación
- ❌ Conjuro — mask `R`
- ❌ Insolencia — mask `R`, mask `r`
- ❌ Desdén — mask `R`
- ❌ Odisea
- ❌ Éxodo
- ❌ Cábala — mask `R`, mask `r`
- ❌ Resiliencia — mask `R`
- ❌ Aflicción — mask `R`
- ❌ Ofensiva
- ❌ Resonancia — effect 1181 (places a portal (+#3% damage, +#1% per cell travelled)), effect 1182 (Teleportal)
- ❌ Vestigio
- ❌ Mofa — effect 320 (steals #1-#2 range), mask `R`, mask `r`
- ❌ Sinecura — mask `R`
- ❌ Extinción — mask `R`
- ❌ Sermón — mask `R`, mask `r`
- ❌ Ridículo — mask `R`
- ❌ Sarcasmo — mask `R`
- ❌ Ayuda Mutua
- ❌ Coalición — mask `R`, mask `r`

</details>

<details><summary><b>Hipermago</b> — 0 of 44 seen working, 8 resolve on paper</summary>

- ❌ Onda Sísmica — effect 320 (steals #1-#2 range)
- ❌ Tizón — effect 320 (steals #1-#2 range)
- ❌ Éter — effect 320 (steals #1-#2 range)
- ❌ Catarata — effect 320 (steals #1-#2 range)
- ❌ Runificación — effect 2023 (triggers runes), mask `c`
- ❌ Manifestación — effect 2023 (triggers runes)
- ❌ Lanzallamas — effect 320 (steals #1-#2 range)
- ❌ Lanzas Telúricas — effect 320 (steals #1-#2 range)
- ❌ Estalagmita — effect 320 (steals #1-#2 range)
- ❌ Onda Celeste — effect 320 (steals #1-#2 range)
- ❌ Tormenta — effect 320 (steals #1-#2 range)
- ❌ Huracán — effect 320 (steals #1-#2 range)
- ❌ Lanza Solar — effect 320 (steals #1-#2 range)
- ❌ Cometa — effect 320 (steals #1-#2 range), mask `c`
- ❌ Polaridad
- ❌ Convección
- ❌ Trazo Flamígero — effect 320 (steals #1-#2 range)
- ❌ Estalactita — effect 320 (steals #1-#2 range)
- ❌ Glaciar — effect 320 (steals #1-#2 range)
- ❌ Volcán — effect 320 (steals #1-#2 range)
- ❌ Propagación — effect 320 (steals #1-#2 range)
- ❌ Prisma Rúnico — effect 2023 (triggers runes), effect 786 (heals the attacker for #1% of the damage)
- ❌ Escudo Elemental — effect 320 (steals #1-#2 range), mask `H`, mask `o`
- ❌ Guardián Elemental
- ❌ Hoja Astral — effect 320 (steals #1-#2 range)
- ❌ Deflagración — effect 320 (steals #1-#2 range)
- ❌ Contribución
- ❌ Impronta — effect 798 (#1: objetivo visible necesario activado), mask `c`
- ❌ Diluvio — effect 320 (steals #1-#2 range)
- ❌ Asteroide — effect 320 (steals #1-#2 range)
- ❌ Sobrecarga Rúnica — effect 2023 (triggers runes)
- ❌ Sublimación — effect 320 (steals #1-#2 range), mask `O`
- ❌ Ráfaga — effect 320 (steals #1-#2 range)
- ❌ Brecha — effect 320 (steals #1-#2 range)
- ❌ Meteoro — effect 320 (steals #1-#2 range)
- ❌ Avalancha — effect 320 (steals #1-#2 range)
- ❌ Ciclo Elemental — effect 320 (steals #1-#2 range)
- ❌ Corriente Cuadramental — effect 320 (steals #1-#2 range)
- ❌ Travesía
- ❌ Repulsión Rúnica — effect 2023 (triggers runes)
- ❌ Drenaje Elemental
- ❌ Tributo
- ❌ Supernova — effect 2832 (#1-#2 damage of the peor elemento), effect 320 (steals #1-#2 range)
- ❌ Torrente Arcano

</details>

<details><summary><b>Uginak</b> — 0 of 44 seen working, 3 resolve on paper</summary>

- ❌ Convergencia
- ❌ Busca
- ❌ Presa — effect 1045 (#1: cooldown pinned to #3 turns), effect 786 (heals the attacker for #1% of the damage)
- ❌ Animal de Caza — effect 1045 (#1: cooldown pinned to #3 turns)
- ❌ Moloso — effect 2905 (#1: alcance máximo fijado en #3), effect 2906 (#1: alcance mínimo fijado en #3), mask `J`, mask `L`, mask `M`, shape `G`
- ❌ Mandíbula — effect 2905 (#1: alcance máximo fijado en #3), effect 2906 (#1: alcance mínimo fijado en #3), mask `J`, mask `L`, mask `M`, shape `G`
- ❌ Cúbito — mask `J`, mask `L`, mask `M`, shape `G`
- ❌ Calcáneo — mask `J`, mask `L`, mask `M`, shape `G`
- ❌ Carcasa — mask `J`, mask `L`, mask `M`, shape `G`
- ❌ Batida — mask `J`, mask `L`, mask `M`, shape `G`
- ❌ Ojeo — effect 77 (Roba #1-#2 MP), mask `J`, mask `L`, mask `M`, shape `G`
- ❌ Ladrar — mask `J`, mask `L`, mask `M`, shape `G`
- ❌ Amaine — effect 2905 (#1: alcance máximo fijado en #3), effect 2906 (#1: alcance mínimo fijado en #3)
- ❌ Afección — effect 2905 (#1: alcance máximo fijado en #3), effect 2906 (#1: alcance mínimo fijado en #3)
- ❌ Lanzagozquetes — mask `U`
- ❌ Gangrena — effect 2905 (#1: alcance máximo fijado en #3), effect 2906 (#1: alcance mínimo fijado en #3)
- ❌ Dogo — mask `J`, mask `L`, mask `M`, shape `G`
- ❌ Restos — mask `J`, mask `L`, mask `M`, shape `G`
- ❌ Tibia — effect 2905 (#1: alcance máximo fijado en #3), effect 2906 (#1: alcance mínimo fijado en #3), mask `J`, mask `L`, mask `M`, shape `G`
- ❌ Húmero — effect 2905 (#1: alcance máximo fijado en #3), effect 2906 (#1: alcance mínimo fijado en #3), mask `J`, mask `L`, mask `M`, shape `G`
- ❌ Rastreo — mask `J`, mask `L`, mask `M`, shape `G`
- ❌ Despiece — effect 2905 (#1: alcance máximo fijado en #3), effect 2906 (#1: alcance mínimo fijado en #3), mask `J`, mask `L`, mask `M`, shape `G`
- ❌ Sabueso — mask `J`, mask `L`, mask `M`, shape `G`
- ❌ Tetanización — effect 2905 (#1: alcance máximo fijado en #3), effect 2906 (#1: alcance mínimo fijado en #3), mask `J`, mask `L`, mask `M`, shape `G`
- ❌ Arcanino — effect 2905 (#1: alcance máximo fijado en #3), effect 2906 (#1: alcance mínimo fijado en #3)
- ❌ Caninos — effect 2905 (#1: alcance máximo fijado en #3), effect 2906 (#1: alcance mínimo fijado en #3)
- ❌ Pelaje Protector — effect 2905 (#1: alcance máximo fijado en #3), effect 2906 (#1: alcance mínimo fijado en #3)
- ❌ Ferocidad — mask `c`
- ❌ Carroña — effect 2905 (#1: alcance máximo fijado en #3), effect 2906 (#1: alcance mínimo fijado en #3), mask `J`, mask `L`, mask `M`, shape `G`
- ❌ Radio — effect 2905 (#1: alcance máximo fijado en #3), effect 2906 (#1: alcance mínimo fijado en #3), mask `J`, mask `L`, mask `M`, shape `G`
- ❌ Hueso con Tuétano — mask `J`, mask `L`, mask `M`, shape `G`
- ❌ Bozal — effect 1019 (#1), mask `J`, mask `L`, mask `M`, shape `G`
- ❌ Pánico
- ❌ Caza — effect 1165 (places to glyph)
- ❌ Amarok — mask `J`, mask `L`, mask `M`, shape `G`
- ❌ Cerbero — mask `J`, mask `L`, mask `M`, shape `G`
- ❌ Ladrido — effect 2905 (#1: alcance máximo fijado en #3), effect 2906 (#1: alcance mínimo fijado en #3)
- ❌ Enojo — effect 2905 (#1: alcance máximo fijado en #3), effect 2906 (#1: alcance mínimo fijado en #3)
- ❌ Cacería — mask `J`, mask `L`, mask `M`, shape `G`
- ❌ Vértebra — mask `J`, mask `L`, mask `M`, shape `G`
- ❌ Olfacción — effect 2905 (#1: alcance máximo fijado en #3), effect 2906 (#1: alcance mínimo fijado en #3)
- ❌ Ensañamiento — effect 2905 (#1: alcance máximo fijado en #3), effect 2906 (#1: alcance mínimo fijado en #3)
- ❌ Clamor de la Manada — effect 1036 (#1: -#3 cooldown), mask `c`
- ❌ Luna Nueva — effect 2905 (#1: alcance máximo fijado en #3), effect 2906 (#1: alcance mínimo fijado en #3)

</details>

<details><summary><b>Forjalanza</b> — 0 of 44 seen working, 9 resolve on paper</summary>

- ❌ Lanza del Lago — shape `G`
- ❌ Chuzo Sísmico — shape `G`, shape `R`
- ❌ Lanzapiedras — shape `G`
- ❌ Jabalina Rayo — shape `G`
- ❌ Epílogo — shape `G`
- ❌ Anticipación — mask `O`, shape `G`
- ❌ Lanza de Incendios — shape `G`
- ❌ Lluvia Dorena — shape `G`
- ❌ Carga Heroica — shape `G`, shape `O`
- ❌ Galantería — shape `G`
- ❌ Colapso — shape `*`
- ❌ Lanza Ciclón — shape `G`
- ❌ Al Tridente — shape `F`
- ❌ Maelstrom
- ❌ Falange — mask `c`
- ❌ Oriflama — shape `G`
- ❌ Estocada Ardiente
- ❌ Octava
- ❌ Golpiza de Bronce
- ❌ Sublevación
- ❌ Balestra
- ❌ Molino de Viento
- ❌ Talón de Barro — shape `G`
- ❌ Posición de Fondo — shape `G`
- ❌ Kyrja — shape `G`
- ❌ Vajra — shape `G`
- ❌ Muspel — shape `G`
- ❌ Ydra — shape `*`, shape `G`
- ❌ Punzón — mask `c`, shape `G`
- ❌ Abrazo de Valquíride — mask `H`, mask `O`
- ❌ Tierra Media — shape `G`
- ❌ Despeje — shape `G`
- ❌ Caballería — shape `G`
- ❌ Renombre — shape `G`
- ❌ Jormun — mask `c`, shape `G`
- ❌ Cadena Candente — shape `G`
- ❌ Preludio al Hierro
- ❌ Crepúsculo
- ❌ Noa — shape `G`
- ❌ Elding — shape `G`
- ❌ Eclipse — effect 1036 (#1: -#3 cooldown), effect 289 (#1: line of sight disabled), effect 2905 (#1: alcance máximo fijado en #3), effect 2906 (#1: alcance mínimo fijado en #3), effect 299 (#1: casilla libre necesaria activada), effect 314 (#1: casilla ocupada necesaria activada), mask `c`, shape `G`
- ❌ Holmgang — effect 2018 (dispels glyphs), shape `G`
- ❌ Jabalina Keatina — shape `G`
- ❌ Molino Rojo — shape `O`

</details>

### 🎯 Combat challenges

- ✅ The preparation dance, measured across 305 captures with both directions on one timeline: two candidates with a 15-second timer, the player marks and validates, and the server fixes whatever is left when you declare ready
- ✅ **15 of the 16** watched live, with every rule taken from the challenge's own translated description
- ✅ Results travel the moment they happen — a failure the instant the challenge breaks, a success at the end, a defeat failing them all at once
- ✅ The bonus is folded into experience, kamas and drop rates on a win; it is not itemised anywhere on the wire
- ✅ Dungeon and anomaly challenges are imposed at 0% and carry achievements, written once and never offered again
- ❌ *Hired Killer* (35), which needs the server to designate and re-designate the target
- ❌ Challenges without a measured percentage — the client ships no bonus field, and the same challenge appears at 90 and at 150 always at +60, so there is a per-fight modifier nobody has reconstructed

### ❌ Not implemented at all

- Crafting professions
- Achievements
- Guilds
- Party fights

---

## 🛠️ Jondo Studio
<img width="2560" height="1508" alt="image" src="https://github.com/user-attachments/assets/14ee4541-d473-4bd1-81dd-617d03c8ba82" />
<img width="2558" height="1502" alt="image" src="https://github.com/user-attachments/assets/21917c8d-4e7a-43a1-a7bc-73dddee31137" />
<img width="2558" height="1496" alt="image" src="https://github.com/user-attachments/assets/39afe77a-c451-43a2-a67b-8ab5e6abe4c4" />
<img width="2558" height="1508" alt="image" src="https://github.com/user-attachments/assets/c139b38c-232d-4a58-9f45-572e643ccd93" />


> ⚠️ **Very early.** The Studio changes every day, and the parts that write files have been exercised
> by one person on one machine. Read it, use it, tell us what is wrong — but keep a copy of
> `content/` before a long session, and expect screens to move under you. Nothing in it can damage
> `world.db` or a running server, which is the one guarantee it does make.

The world editor. A third executable next to the launcher and the server, and it needs neither of
them running: it opens `content/` and the data files through the same paths the server uses and
works on its own. Built with **Avalonia**, so it runs on Windows, macOS and Linux.

It unpacks `world.db` from `datos/world.zip` the first time it runs, the way the server does, so a
fresh clone can open it and see the world without starting anything else.

It exists because of a problem this project could not solve any other way. The client holds a great
deal — every item, every spell, every monster — but there are things it has never held, because on
the real game they were the server's: which reply in a dialogue leads to which line, where an NPC
stands and what it does there, which interactive teleport comes back to which map. Those cannot be
extracted. They have to be **decided**, and until now the only place to decide them was a Python
script and a JSON file nobody could review.

### Three layers, and every row says where it came from

The data lives in three places that cannot be edited the same way: `dofus3_data/` is a raw dump of
the client, `datos/*.json` is regenerated by the tools in `tools/`, and `world.db` is a 240 MB
binary no pull request can review. A hand edit in any of them disappears the next time somebody
runs a script.

So there are three layers, merged on load, and only the last one is ever edited:

| layer | where from | who edits it |
|---|---|---|
| **base** | generated from the client dump | nobody |
| **measured** | learned from packet captures | nobody |
| **authored** | decided by a person | this is the one, and it always wins |

The authored layer is `content/`, in versioned JSON, so a change is a reviewable diff and two people
can edit different maps without colliding. It stores **deltas, not copies**, and it can *erase* a
row it did not write.

**Every row carries its provenance**, and that column is the point: six months from now nobody will
remember whether a cell number was measured off a capture or typed in by hand, and without it on
screen the two become indistinguishable.

### What it does today

Nine sections, **in Spanish, English or French** — and the language switch changes both halves at
once. The editor's own words come from one catalogue; the game's words are read straight out of the
client's `Content/I18n/{lang}.bin`, 339,342 texts per language. The format is not documented
anywhere; it was worked out and then checked against `world.db`, where 500 keys sampled at random
came back byte for byte identical, including one of 42,180 characters.

**The creatures are drawn**, out of the client's own bundles and nothing copied into the repository.
Monsters come from a picto atlas, 5,130 of the 5,134 covered. NPCs are assembled the way the client
assembles them: bones, a still frame, and the skins the look names. That renderer now lives in its
own project, `Jondo.Unity.Sprites`, and the launcher draws its account portraits with it.

- ✅ **Overview** — which files it read and what came out of each. First screen on purpose
- ✅ **Traffic** — the client-server conversation, live and back through the log, every frame read **against the protocol the client itself declares**. From here a packet can be named on the spot, from the **513 real message names** the client still ships in its metadata
- ✅ **Packets** — every kind of packet seen, with a status ladder: unknown, named, documented, handled, ignored
- ✅ **NPCs** — all 422 placements, with the provenance column and the NPC drawn on the map
- ✅ **Dialogues** — which reply leads to which line, with the text on screen rather than ids
- ✅ **Monsters** — open a group, take a monster out, put another in, move it two cells left
- ✅ **Spells** — every spell with its effects, and the map showing **how far it reaches and what it would hit**, worked out by calling the fight engine's own `Zone.Casillas` rather than a drawing of it
- ✅ **Passages** — two maps side by side, a door picked on each, and one button that joins them **both ways**
- ✅ **Map cells** — the three layers painted one at a time, click to toggle and **drag to paint a run**
- ✅ A section that fails shows its error *inside* the editor, and `Jondo Studio.exe --selftest` builds all nine in all three languages against the real data and fails the publish if any throws

**Everything it writes goes to `content/`**, in versioned text. Nothing opens `world.db` for writing
and nothing talks to a running server.

### What is being worked on

- 🚧 **NPC actions per placement** — the right-click menu is drawn by the *client* from the
  template's `actions[]`, so an action written per placement can only take options away, never add
  one
- 🚧 **Editing spells.** The simulator is there; changing a spell's numbers is not
- 🚧 **Shops, loot tables and dungeons** — all three are screens over data the server already reads
- 🚧 **Editing quests.** The engine plays them and the Studio shows them, but nothing writes one yet
- 🚧 **A thin admin channel** so a running server can be told to reload one domain, without a restart

The full plan is in **`docs/world-editor.md`**.

---

## 🧪 Tests

`Jondo.Unity.Tests` — **1,057 xUnit tests** across 122 files, grouped by domain: `Auth`, `Combat`,
`Content`, `Diagnostics`, `Economy`, `Launcher`, `Movement`, `Network`, `Protocol`, `Quests`,
`Security`, `Sessions`, `Sprites`, `Studio`, `World`. They run in about half a minute.

```bash
dotnet test Jondo.Unity.Tests
```

Five of them run against `logs/gameserver_traffic.log` itself when it is on the machine, and skip
when it is not. A test that skips proves nothing, and that is the trade being made on purpose:
frames this project builds itself only ever prove that the builder and the reader agree, so a
handful of checks are pointed at traffic the real client produced.

**Publishing the server runs them first and fails if any is red.** Not on build — the inner loop
stays fast — but publishing is the one step between writing code and a player running it. The escape
hatch is `-p:SkipTests=true`, which leaves its trace on the command line rather than in a config
file nobody reads.

### Three kinds of check, three homes

* **At startup, and it throws** stay the questions of the form *"is the data I was shipped sane?"* —
  the fight sheet's 53 characteristics in their captured order, the interactive registry, the monster
  spellbooks, the vendor placements, the profession catalogue. `datos/` and `world.db` are
  regenerated by tooling outside the build, so a bad regeneration reaches a player with every test
  still passing.
* **In the test project** live the questions of the form *"is this code correct?"* — the content
  layers, the collision damage formula, the Jondo Coin bands, frame limits, protobuf parsing,
  password hashing, log censorship and session isolation.
* **Architecture tests** ask *"is this code shaped right?"* — they read the fight engine's own
  source and fail on the shapes a multi-client engine cannot afford. They are the only kind that
  catches a mistake **before** it has a symptom, and they hold an exception list where every entry
  carries a written reason.

Some things cannot be asserted by asking whether an operation succeeded, because it always does: a
portrait that draws a character facing away, or with no head, is still a valid PNG. Those are
guarded by counting — the animation name has to end in the direction that faces the camera, and the
head slot has to contribute more than zero triangles.

---

## 🔎 Surviving the next patch

Every protobuf message in Dofus 3 is named with three random letters — `kub`, `jru`, `lqu` — and on some patches Ankama reshuffles the lot. Nothing else about the protocol changes shape, but the emulator no longer knows what anything is called. **`protocolbuilder`** is the command line for that; **`Jondo Desofuscador.exe`** is the same engine behind one window and one button.

Eight consecutive real clients (3.6.4.3 → 3.6.10.10) were pulled from Ankama's own CDN and compared patch by patch:

- **Ankama does not reshuffle on every patch.** Three of the seven jumps keep all 2,169 names, one for one — five obfuscation generations across eight versions. The tool checks for the identity mapping first, in a second.
- **Zero wrong pairings over 6,505 real pairs.** The matcher never looks at names, only at field numbers, kinds and neighbourhood. It gets 71.1% and misses none; what it cannot decide, it leaves alone.
- **On a patch that does reshuffle, structure alone gets about 11%** — the ceiling, not a tuning problem.
- **Chaining through intermediate versions is worse**: 12 pairs against 245 for the direct jump. A plausible idea the measurement refuted.
- Building the `Op` layer also turned up **49 opcodes that only exist in 3.6.4.3**.

The **`Op` layer** replaced **495 three-letter literals across 35 files** with one generated file, `Jondo.Unity.Protocol/Op.cs`, so applying a mapping never means editing the emulator by hand.

```bash
protocolbuilder proto    <client dll> [out.proto]      the client's own message shapes
protocolbuilder mapear   <old client> <new client>     who is who between two versions
protocolbuilder capa     <client> <anchors> . --aplicar  regenerate Op.cs and migrate call sites
protocolbuilder bajar    3.6.4.3 3.6.10.10 clientes    fetch old clients from the CDN, 183 MB each
protocolbuilder cadena   clientes                      measure each patch on its own
```

> `proto` earns its keep beyond migrations. What a message carries is settled by the client's
> own schema rather than by one reading of one capture: `lth { bool, bool }` is two booleans,
> and no amount of staring at two bytes on the wire says that as plainly.

Full write-up in `docs/desofuscacion.md`.

---

## 🧱 Source layout

The three executables:
* **`Jondo.Unity.Server`** → `Jondo Server.exe` — proxies, network parser, handlers, managers, database and the server's log window. The spell effect engine lives in `Managers/`: `SpellEffects` reads the spell data, `EffectEngine` turns it into things that happen to somebody, and `Summons` builds summoned fighters from monster templates
* **`Jondo.Unity.Launcher`** → `Jondo Emulator Launcher.exe` — the player's window, in Avalonia. References the contract and the sprite renderer, and nothing else
* **`Jondo.Unity.Studio`** → `Jondo Studio.exe` — the world editor, in Avalonia

Shared:
* **`Jondo.Unity.Contract`** — paths, settings and the shared palette
* **`Jondo.Unity.Contract.WinForms`** — what is left of the old Windows Forms shell, kept apart so nothing else drags it in
* **`Jondo.Unity.Core`** — networking infrastructure and TCP servers
* **`Jondo.Unity.Auth`** — authentication and HAAPI handlers
* **`Jondo.Unity.Protocol`** — message definitions and the generated `Op` layer
* **`Jondo.Unity.World`** — world logic, `FightInstance`, the fight rulebooks (`FightRules`), buffs and states (`Buff`), area shapes and displacement (`Zone`), isometric geometry (`MapGeometry`)
* **`Jondo.Unity.Sprites`** — draws a character or an NPC out of the client's own bones, skins and atlases. Shared by the Studio and the launcher so a fix to either reaches both
* **`Jondo.Unity.Parser`** — capture parsing
* **`Jondo.Unity.Tests`** — 1,057 xUnit tests, and the gate on publishing

The protocol toolchain, which the emulator does not depend on:
* **`Jondo.Unity.Reversing`** — reads a client with Cpp2IL, rebuilds the `.proto`, matches two versions, indexes the code, downloads old clients from the CDN (`Cytrus`) and generates the `Op` layer
* **`Jondo.Unity.ProtocolBuilder`** → `protocolbuilder` · **`Jondo.Unity.Deobfuscator`** → `Jondo Desofuscador.exe`
* **`JondoFix`** — the MelonLoader client mod, source plus the compiled dll

Documentation, all of it measured rather than assumed — index in `docs/README.md`. Start with `docs/protocol.md` (how a message travels), `docs/opcodes.md` (what each opcode means and where it was seen), `docs/fight.md` (a fight on the wire, opcode by opcode) and `docs/desofuscacion.md` (surviving a patch).

---

## 💾 Database and persistence

Three **SQLite** databases in `bases/`, and one folder of text:

* **`world.db`** — 41 tables and 659,397 rows: characters, inventories, positions, map persistence, spells, monsters, appearances, wardrobe and haven bags. Distributed compressed as `datos/world.zip` (24.8 MB) and extracted on first run.
* **`auth.db`** — accounts and authentication sessions, created on first run.
* **`paquetes.db`** — the packets the server does not yet know how to answer, deduplicated by protobuf shape. Kept apart on purpose: it carries nothing needed to play, it can be deleted to start over, and it can be handed to somebody else to look at without handing over anybody's characters.
* **`content/`** — the authored layer, in versioned JSON. The only one edited by hand, and the only one nothing regenerates. See [Jondo Studio](#-jondo-studio).

Files are looked up in `datos/`, then `bases/`, then the root, so a half-moved installation still starts.

**Some regression guards also run at startup and throw**, so the server refuses to boot when the data it was shipped does not match what the code expects — see [Tests](#-tests) for which checks live where, and why.
