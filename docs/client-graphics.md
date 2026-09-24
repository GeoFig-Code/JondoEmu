# Client texture packs and DirectX

How Dofus **3.6.10.10** selects its map texture pack when it is started outside the
Ankama Launcher, and why this selection is independent from the DirectX renderer.

---

## 1. Two independent settings

The texture pack and the graphics API are selected by different command-line arguments:

| Purpose | Argument | Required client content |
|---|---|---|
| Standard map textures | no quality argument | `Dofus_Data/StreamingAssets/Content/Map/Textures/1x/` |
| HD map textures | `--hdReady` | `Dofus_Data/StreamingAssets/Content/Map/Textures/2x/` |
| 4K map textures | `--4kReady` | `Dofus_Data/StreamingAssets/Content/Map/Textures/4x/` |
| DirectX 12 | no renderer argument | the client's default on Windows |
| DirectX 11 | `-force-d3d11` | a GPU and driver supporting DirectX 11 |

`--hdReady` does not enable DirectX 11, and `-force-d3d11` does not enable HD textures. They may
be combined:

```text
Dofus.exe -force-d3d11 --hdReady ...
```

This mapping is declared by the client itself in `zaap.yml` and `zaap-windows.yml`. The former
adds the `map_textures_2x` fragment and the `--hdReady` argument when the Launcher's `hdReady`
quality setting is enabled. The latter maps the DirectX 11 selection to `-force-d3d11`.

---

## 2. Installing a pack is not enough

An unpacked client may contain the HD bundles without using them. Two conditions must be true:

1. the matching texture directory and its catalog and bundles must be present;
2. the process must receive the matching readiness argument at startup.

For HD, check that the following directory exists and is not empty:

```text
Dofus_Data/StreamingAssets/Content/Map/Textures/2x/
```

The directory contains `catalog_1.0.bin`, `catalog_1.0.hash` and
`mapgfx_2x_*_assets_all.bundle` files in the inspected 3.6.10.10 client. Their presence proves that
the pack is installed; it does not prove that the running process was told to select it.

Do not pass `--4kReady` merely because `catalogs.json` lists `Textures/4x`. That file declares the
catalogue families the client understands. The actual 4K pack is installed only when the
`Content/Map/Textures/4x/` directory and its bundles are present.

---

## 3. Jondo launch paths

### Local batch launcher

The local `launch_jondo_Drago.bat` next to `Dofus.exe` must include `--hdReady` in the continued
argument list:

```bat
"Dofus.exe" ^
  -force-d3d11 ^
  --hdReady ^
  --port "%ZAAP_PORT%" ^
  ...
```

Keep the trailing `^` on every continued line except the last one. Removing it makes the remaining
arguments separate shell commands instead of Dofus arguments.

### Native Jondo launcher

File: `Jondo.Unity.Launcher/LauncherService.cs`

`LauncherService.LaunchClient` currently builds its command line with `-force-d3d11`, window size,
MelonLoader, Zaap and connection arguments. It does **not** append `--hdReady` or `--4kReady`.
Consequently, clients started through the native launcher use the standard map texture selection
even when the optional 2x bundles exist on disk.

Adding HD support to the native launcher requires appending `--hdReady` to the `arguments` string.
A future user-facing quality selector should verify that the corresponding texture directory
exists before adding the flag, so a saved HD or 4K preference cannot point at a pack that was not
copied into the selected client.

---

## 4. Verification

Close every Dofus process before changing launch arguments, then start a fresh client. Existing
processes cannot change pack or renderer at runtime.

From PowerShell, the installed map packs can be listed without modifying the client:

```powershell
Get-ChildItem '<client-root>\Dofus_Data\StreamingAssets\Content\Map\Textures' -Directory
```

To inspect a local batch launcher's relevant arguments:

```powershell
Select-String '<client-root>\launch_jondo_Drago.bat' -Pattern 'force-d3d|hdReady|4kReady'
```

Expected HD and DirectX 11 output contains both independent switches:

```text
-force-d3d11
--hdReady
```

If the client still displays standard map textures, verify that the launched `Dofus.exe` belongs
to the same client root whose `Textures/2x` directory was inspected. Jondo can store an explicit
client executable path in `%APPDATA%\Jondo\lanzador.cfg`, so the native launcher may be starting a
different installation.

---

## 5. Cost of higher-resolution packs

The HD and 4K switches change map assets, not networking or emulator behaviour. They can increase
disk use, VRAM use and map loading time. This is especially relevant when Jondo starts several
independent clients: each process loads and renders its own assets. DirectX 11 may be selected for
compatibility without giving up the HD pack.
