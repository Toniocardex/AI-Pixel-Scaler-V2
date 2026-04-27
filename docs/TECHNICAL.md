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
- **Views:** `MainWindow` — pannelli per moduli 1–7, `EditorSurface`, menu file; `SandboxWindow` — mini-engine di prova.
- **Controls:** `EditorSurface` (canvas), `SandboxView` (gioco semplice).
- **File e export:** `StorageProvider` (Avalonia) per aprire/salvare; export PNG/JSON in code-behind `MainWindow`.

L’elaborazione pixel avviene **nel processo .NET** (nessun WebView). ImageSharp lavora su `Image<Rgba32>`; la UI usa `Rgba32BitmapBridge` (PNG in memoria) per produrre `Avalonia.Media.Imaging.Bitmap` dove serve.

---

## 5. Riferimenti regole e matematica

I vincoli su formule (AABB, pivot, cinematica, nearest-neighbor, ecc.) sono in [.cursor/rules/ai-pixel-scaler-roadmap-math.mdc](../.cursor/rules/ai-pixel-scaler-roadmap-math.mdc).

---

## 6. Build

- Sviluppo: `dotnet run --project src/AiPixelScaler.Desktop/AiPixelScaler.Desktop.csproj`
- Flusso consigliato: `.\scripts\ai-pixel-scaler-flow.ps1` oppure `.\ai-pixel-scaler-flow.cmd` dalla root; con `-Publish` o `ai-pixel-scaler-flow.cmd publish` per eseguibile in `dist/win-x64`.

*Per la mappatura con l’altra app, vedi [STRUCTURE.md](STRUCTURE.md).*
