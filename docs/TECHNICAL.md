# AI Pixel Scaler — Documentazione tecnica (.NET / Avalonia)

Questo documento descrive l’implementazione **corrente** del repository: elaborazione immagini e moduli editor in **C#**, shell desktop **Avalonia 12**, buffer **ImageSharp** (`Rgba32`).

La descrizione di un **progetto fratello** basato su React + Vite + Tauri è conservata in [TECHNICAL.legacy-web.md](TECHNICAL.legacy-web.md).

---

## 1. Stack

| Area | Tecnologia |
|------|------------|
| Runtime | .NET 8 |
| UI desktop | Avalonia 12 (Fluent theme) |
| Immagini | SixLabors.ImageSharp 3.x |
| Test | xUnit (`tests/AiPixelScaler.Core.Tests`) |
| Build / publish | MSBuild; script [scripts/ai-pixel-scaler-flow.ps1](../scripts/ai-pixel-scaler-flow.ps1) (test + build; opzione `-Publish` per `dist/win-x64`) |

---

## 2. Soluzione e progetti

- [AiPixelScaler.sln](../AiPixelScaler.sln) — solution unica.
- **AiPixelScaler.Core** — libreria: geometria, pipeline immagine, editor (viewport), sandbox (fisica/animazione).
- **AiPixelScaler.Desktop** — eseguibile WinExe: `Program.cs`, `App.axaml`, finestre in `Views/`, controlli in `Controls/`, bridge verso Avalonia in `Imaging/`.

---

## 3. Struttura logica Core

- **`AiPixelScaler.Core.Geometry`** — AABB (`AxisAlignedBox`), intersezioni.
- **`AiPixelScaler.Core.Pipeline.*`** — pipeline di post-processing e export (allineata al concetto di `src/lib` nell’app web):
  - `Pipeline.Imaging` — caricamento, AABB alpha, denoise isole.
  - `Pipeline.Effects` — onion blend, tint, affine 2D.
  - `Pipeline.Normalization` — crop atlas, layout globale, pivot draw.
  - `Pipeline.Slicing` — griglia, CCL, `SpriteCell`.
  - `Pipeline.Export` — atlas packer, JSON.
- **`AiPixelScaler.Core.Editor`** — `Viewport2D` (zoom/pan).
- **`AiPixelScaler.Core.Sandbox`** — cinematica, animazione, mondo 2D (mini-engine).

---

## 4. Desktop: shell e viste

- **Shell:** `App.axaml` / `App.axaml.cs` imposta `MainWindow` come finestra principale.
- **Views:** `MainWindow` - shell temporanea con toolbar, workspace e routing Studio; `StartPageView` - selezione iniziale Sprite/Animation/Tileset; `Views/Studios/SpriteStudioView` - pannello operativo Sprite collegato agli handler esistenti; `EditorSurface`; `SandboxWindow` - mini-engine di prova.
- **Controls:** `EditorSurface` (canvas), `SandboxView` (gioco semplice).
- **File e export:** `StorageProvider` (Avalonia) per aprire/salvare; export PNG/JSON in code-behind `MainWindow`.

L’elaborazione pixel avviene **nel processo .NET** (nessun WebView). ImageSharp lavora su `Image<Rgba32>`; la UI usa `Rgba32BitmapBridge` (PNG in memoria) per produrre `Avalonia.Media.Imaging.Bitmap` dove serve.

### 4.1 Refactor Studio corrente

La shell desktop sta migrando in modo progressivo verso tre aree operative:

- **Sprite Studio:** sprite statici, cleanup, ROI, slicing, palette/quantize, export sprite.
- **Animation Studio:** frame multipli, preview animazione, timeline, pivot, baseline, anti-jitter e `Video Frame Extractor` (implementato).
- **Tileset Studio:** griglie, crop-to-grid, template, seamless/tile preview ed export Tiled.

`MainWindow` resta per ora la shell di compatibilita': gestisce toolbar, workspace condiviso, stato documento e routing tra Start Page e Studio attivo. La logica applicativa viene estratta gradualmente in controller dedicati sotto `AiPixelScaler.Desktop/Controllers`.

`SpriteStudioView` e' la prima view Studio introdotta: espone import, cleanup, ROI, slicing, floating paste, quantize, resize/mirror ed export come comandi visibili. In questa fase richiama ancora gli handler di `MainWindow`, cosi' la migrazione UI puo' procedere senza cambiare comportamento o nascondere funzioni. Il blocco ROI nella view Sprite sincronizza lo stato della selezione corrente e richiama select all, clear, export ROI, crop e remove tramite il `SelectionController` gia' estratto. Il blocco Slicing sincronizza crop manuale, righe/colonne griglia, celle rilevate, export frame e stato atlas pulito con il backplane legacy e il `SlicingController`. Dopo la verifica end-to-end, i tab legacy `Slice` e `Selezione` sono stati rimossi e il loro stato e' gestito da `SpriteStudioView` e campi interni della shell. Il blocco Cleanup/Filtri espone preset, Rimozione sfondo, Edge Refinement, Denoise e Quantize nella view Sprite; prima dell'esecuzione sincronizza lo stato nel runtime e riusa la pipeline esistente. Il blocco trasformazioni/export espone resize nearest, mirror H/V, export PNG e JSON.

`TilesetStudioView` e' la seconda view Studio migrata: espone palette/stilizza, seamless/tile preview, pad-to-multiple, griglia template, import frame, crop/POT, snap celle ed export Tiled. Il tab legacy `Tileset` e' stato rimosso; `MainWindow` mantiene un `_tilesetState` come backplane temporaneo e gli handler esistenti leggono da quello stato invece che da controlli XAML legacy. Pivot template e preset di dimensione griglia sono gestiti direttamente dalla view Tileset.

### 4.2 Filtri Sprite Studio

Il pannello di pulizia Sprite distingue i filtri reali dai preset:

- **Preset:** `Default`, `Sicuro`, `Aggressivo` sono ricette che impostano valori iniziali modificabili.
- **One-click cleanup:** esegue pulizia rapida senza rimozione sfondo bordo automatica.
- **Rimozione sfondo:** usa `BackgroundIsolation` come unico filtro dedicato: flood fill dal bordo, tolleranza colore, soglia bordi e alpha threshold opzionale.
- **Edge Refinement:** raggruppa defringe e outline.
- **Denoise:** raggruppa median, isole minime e denoise a maggioranza 3x3.
- **Quantize:** filtro autonomo con colori massimi e metodo (`Wu`, `Octree`); non viene attivato automaticamente dai preset.

### 4.3 Animation Studio: Video Frame Extractor (MVP implementato — 2026-05-03)

`Video Frame Extractor` e' disponibile in `Animation Studio` tramite il pulsante **"Da video…"** nel pannello Export / Import.

Scope MVP implementato:

- input MP4 H.264 tramite dialog dedicato (`VideoImportDialog`);
- metadati base letti via ffprobe (durata, FPS sorgente, dimensioni) mostrati nel dialog;
- range start/end configurabile (fine vuota = intero video);
- estrazione per FPS target o ogni N frame (filtro ffmpeg `fps=` oppure `select`);
- output PNG in cartella temporanea (`%TEMP%\AiPixelScaler_Vid_XXXXXXXX`), cartella rimossa automaticamente al termine;
- import automatico nella timeline come atlas (griglia √n×√n celle, ID `frame_000…`).

Architettura tecnica:

- `Services/FFmpegLocator.cs` — rileva ffmpeg/ffprobe nel PATH di sistema (`where`) o nel percorso configurato manualmente;
- `Services/VideoFrameExtractor.cs` — wrapper `System.Diagnostics.Process`: `GetMetadataAsync` (ffprobe JSON) + `ExtractFramesAsync` (ffmpeg, FPS o every-N, cancellazione via `CancellationToken`);
- `Services/UiPreferencesService.cs` — esteso con campo `FfmpegFolder` nel JSON `%LOCALAPPDATA%\AiPixelScaler\ui-preferences.json`;
- `Views/FfmpegConfigDialog.cs` — dialog "FFmpeg non trovato" con browser cartella e validazione esistenza eseguibili;
- `Views/VideoImportDialog.cs` — dialog import con stima frame live.

Formati futuri (non in MVP): MOV, WebM, AVI.
Fuori MVP: deduplica, scene detection, crop ROI, cleanup batch, export atlas diretto.
Integrazione Sprite: solo destinazione futura secondaria per frame singolo da pulire/slicare.

---

## 5. Riferimenti regole e matematica

I vincoli su formule (AABB, pivot, cinematica, nearest-neighbor, ecc.) sono in [.cursor/rules/ai-pixel-scaler-roadmap-math.mdc](../.cursor/rules/ai-pixel-scaler-roadmap-math.mdc).

---

## 6. Build

- Sviluppo: `dotnet run --project src/AiPixelScaler.Desktop/AiPixelScaler.Desktop.csproj`
- Flusso consigliato: `.\scripts\ai-pixel-scaler-flow.ps1` oppure `.\ai-pixel-scaler-flow.cmd` dalla root; con `-Publish` o `ai-pixel-scaler-flow.cmd publish` per eseguibile in `dist/win-x64`.

*Per la mappatura con l’altra app, vedi [STRUCTURE.md](STRUCTURE.md).*

