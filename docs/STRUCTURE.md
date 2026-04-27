# Struttura repository e mapping con l’app web (legacy)

## Albero atteso (C# / Avalonia)

```text
AI Pixel Scaler v2/
  AiPixelScaler.sln
  src/
    AiPixelScaler.Core/
      Geometry/
      Pipeline/
        Imaging/ Effects/ Normalization/ Slicing/ Export/
      Editor/
      Sandbox/
    AiPixelScaler.Desktop/
      Views/          # MainWindow, SandboxWindow
      Controls/       # EditorSurface, SandboxView
      Imaging/        # bridge Rgba32 → Bitmap
      App.axaml, Program.cs, app.manifest
  tests/
    AiPixelScaler.Core.Tests/
  docs/
  scripts/
  dist/              # output publish (opzionale, .gitignore consigliato)
```

## Mapping concettuale (altra app → questo repo)

| Concetto / percorso (app React + Tauri) | Equivalente in questo repo |
|----------------------------------------|----------------------------|
| `src/lib/*` (pipeline immagine) | `AiPixelScaler.Core.Pipeline.*` |
| `src/App.tsx` (modalità / tab) | `Views/MainWindow` (Expander e comandi per modulo) |
| `src/components/*` | `Desktop/Controls` + XAML in `Views` |
| `src-tauri` (dialog / fs) | API native Avalonia: `IStorageProvider`, salvataggio in `MainWindow` |
| `src/store/*` (Zustand) | Stato oggi in code-behind `MainWindow` (documento, celle, backup); eventuale refactor verso classi servizio o MVVM in futuro |
| Export PNG + JSON | `Pipeline.Export` + `StorageProvider.SaveFilePickerAsync` |
| BFS bordo / Konva / Sprite Studio | Non portati 1:1; moduli C# includono CCL, denoise, sandbox cinematico. |

## Assembly

- **AiPixelScaler.Core** — nessun riferimento a Avalonia; riutilizzabile da test o altre UI.
- **AiPixelScaler.Desktop** — riferisce Core e pacchetti Avalonia / ImageSharp.

## Documentazione storica (web)

Il documento [TECHNICAL.legacy-web.md](TECHNICAL.legacy-web.md) descrive l’altra codebases (React, Tauri) e resta di riferimento per funzionalità non ancora reimplementate in C#.
