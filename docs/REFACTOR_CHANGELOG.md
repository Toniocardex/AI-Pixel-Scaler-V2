# Refactor Changelog

Questo documento riassume il refactor architetturale completato sulle milestone prodotto.

## Obiettivo

- Centralizzare la pipeline immagini in un entry-point unico.
- Ridurre la frammentazione tra UI, Batch e futuri client.
- Preparare il repository a evoluzione CLI e plugin.

## Modifiche principali

### 1) Pipeline unificata (Core)

- Aggiunto orchestratore unico: `src/AiPixelScaler.Core/Pipeline/Imaging/PixelArtPipeline.cs`
  - Contratti stabili:
    - `PixelArtPipeline.Options`
    - `PixelArtPipeline.Report`
- Orchestrazione corrente: BackgroundIsolation, quantize, denoise, island denoise, outline, alpha threshold.

### 2) Riduzione duplicazioni UI

- `src/AiPixelScaler.Desktop/Views/MainWindow.axaml.cs`
  - `RunQuickProcess()` e `RunPixelPipeline()` convergono su flusso comune:
    - `TryBuildPipelineOptions(...)`
    - `ExecutePipeline(...)`
  - Preset safe/aggressive allineati allo stesso entry-point pipeline.

### 3) ViewModel minimo pipeline

- Aggiunto `src/AiPixelScaler.Desktop/ViewModels/PipelineViewModel.cs`
  - Stato parametri pipeline.
  - Preset:
    - `ApplySafePreset()`
- `ApplyAggressivePreset()`
  - Costruzione opzioni:
    - `BuildOptions(...)`

### 4) Batch allineato al contratto unico

- `src/AiPixelScaler.Batch/Program.cs`
  - Processing base ora passa da `PixelArtPipeline`.
  - Mantiene export indexed/shared palette e report batch.

### 5) Skeleton prodotto pubblico

- Nuovo progetto CLI:
  - `src/AiPixelScaler.CLI/AiPixelScaler.CLI.csproj`
  - `src/AiPixelScaler.CLI/Program.cs`
- Nuovo progetto Plugins:
  - `src/AiPixelScaler.Plugins/AiPixelScaler.Plugins.csproj`
  - `src/AiPixelScaler.Plugins/IImageProcessorPlugin.cs`
- Solution aggiornata:
  - `AiPixelScaler.sln` include `AiPixelScaler.CLI` e `AiPixelScaler.Plugins`.

### 6) Documentazione estensioni

- Aggiunto:
  - `docs/PRODUCT_EXTENSIONS.md`
  - descrive contratto pipeline, piano CLI e plugin v1.

## Impatti positivi

- Coerenza: un solo punto di orchestrazione algoritmica.
- Manutenibilità: meno duplicazioni tra quick/full flow.
- Estendibilità: base pronta per CLI e plugin senza riscrivere la logica core.
- Rilasciabilità: build/test/publish restano invariati come processo.

## Verifica eseguita

- Build Release solution: OK
- Test Release: OK
- Publish Desktop win-x64 self-contained:
  - `publish/win-x64`
  - `dist/win-x64`

## Rischi residui

- `MainWindow.axaml.cs` è ancora grande: è consigliato continuare la migrazione verso MVVM completo.
- CLI skeleton è minimale: da espandere con mapping completo delle opzioni pipeline.
- Plugin loader dinamico non è ancora implementato (solo interfaccia v1).

## Prossimi passi consigliati

1. Spostare ulteriormente stato/comandi da `MainWindow` a ViewModel dedicati.
2. Espandere `AiPixelScaler.CLI` con flag completi e comandi batch parity.
3. Implementare plugin loader con version guardrails e isolamento errori.

---

## Sprite Studio — chiusura gap (2026-05-02)

Audit post-implementazione ha rilevato e chiuso tre gap prima di procedere a Tileset Studio.

### Gap 1 — `DefringeOpaque` UI non collegata (risolto)

**Problema:** `TxtSpriteDefringeOpaque` esisteva nel pannello e veniva letto da `GetCleanupState()`, ma il valore non raggiungeva mai i metodi che lo usano. Tre punti hardcodavano `250` ignorando la UI:
- `SyncSpriteCleanupStateFromControls()` — stringa `"250"` fissa
- `RunDefringe()` — `const byte opaque = 250`
- `RunAiCleanupWizard()` — `const byte defOpaque = 250`

**Causa radice:** `PipelineFormState` non aveva un campo `DefringeOpaque`, quindi il valore non poteva essere memorizzato e propagato.

**Soluzione:**
- Aggiunto `string DefringeOpaque = "250"` (con default opzionale) a `PipelineFormState` in `PipelineViewModel.cs`
- `ApplySpriteCleanupStateToControls()` ora salva `state.DefringeOpaque` in `_pipelineFormState`
- `SyncSpriteCleanupStateFromControls()` ora legge `_pipelineFormState.DefringeOpaque`
- `RunDefringe()` e `RunAiCleanupWizard()` calcolano il byte da `_pipelineFormState.DefringeOpaque` con `InputParsing.ParseInt` e clamp `[1, 254]`
- `ToFormState()` in `PipelineViewModel` include `DefringeOpaque: "250"` come default nei flussi preset

### Gap 2 — `BtnGoStilizza` / `BtnGoTemplate` bypassavano `ActivateStudio` (risolto)

**Problema:** I due pulsanti di navigazione verso Tileset usavano `MainTabs.SelectedIndex = 2` direttamente invece di `ActivateStudio(StudioKind.Tileset)`. Conseguenza: `_currentStudio` restava su `Sprite`, `SpriteStudioPanel` rimaneva visibile, il messaggio di stato non veniva aggiornato e `EnterSelectionCanvas()` non veniva chiamato.

**Soluzione:** Sostituiti entrambi con `ActivateStudio(StudioKind.Tileset)` in `MainWindow.axaml.cs`.

### Gap 3 — `ApplyQuantize` eseguiva la pipeline completa (risolto)

**Problema:** Il case `SpriteStudioAction.ApplyQuantize` chiamava `RunPixelPipeline()`, che esegue tutti gli stadi abilitati (rimozione sfondo, denoise, outline, alpha threshold…). L'utente si aspettava solo la riduzione palette.

**Soluzione:** Aggiunto metodo dedicato `RunSpriteQuantize()` in `MainWindow.axaml.cs`:
- Legge `MaxColors` e `QuantizerIndex` da `_pipelineFormState` (sincronizzato via `ApplySpriteCleanupStateToControls()`)
- Chiama direttamente `PaletteExtractorAlgorithms.ExtractWu/ExtractOctree` + `PaletteMapper.ApplyInPlace`
- Nessun dither (coerente col pannello Sprite Studio che non espone dither)
- Il case `ApplyQuantize` ora chiama solo `RunSpriteQuantize()`

**Build post-fix:** 0 errori, 0 warning.

---

## Tileset Studio — pannello operativo iniziale (2026-05-02)

Implementata la prima migrazione UI di Tileset Studio.

**Soluzione:**
- Aggiunto `TilesetStudioAction` con azioni dedicate per palette, seamless, tile preview, pad-to-multiple, template, import frame, crop/POT, snap celle ed export Tiled.
- Aggiunta `TilesetStudioView` con sezioni visibili: flusso, palette, seamless/tile, allineamento dimensioni, griglia template, composizione, export e strumenti avanzati.
- Rimosso il tab legacy `Tileset` da `MainWindow.axaml`; la shell ora mostra `TilesetStudioPanel` quando `ActivateStudio(StudioKind.Tileset)` e' attivo.
- Aggiunto `_tilesetState` in `MainWindow.axaml.cs` per evitare letture dirette da controlli XAML legacy.
- Refactor dei metodi Tileset principali per leggere da `_tilesetState`: `RunPaletteReduce`, `RunMakeTileable`, `RunPadToMultiple`, `BuildTemplateOptions`, `RunCropPipeline`, `RunImportFramesAsync`.
- Pivot template e preset dimensione griglia sono gestiti nel code-behind della view Tileset, non piu' in `MainWindow`.

**Stato residuo:**
- `MainWindow.axaml.cs` resta orchestratore compatibile e contiene ancora handler Tileset; la prossima estrazione consigliata e' un `TilesetStudioController`.
- Il tab `Export` resta come scorciatoia condivisa per export PNG/JSON/ZIP/Tiled finche' non sara' migrato l'export shell.

---

## Animation Studio — pannello operativo iniziale (2026-05-02)

Implementata la migrazione UI di Animation Studio.

**Soluzione:**
- Aggiunto `AnimationStudioAction` con 16 azioni: preview, sandbox, workbench (entra/applica/annulla), align-all (center/baseline/reset), align-selected (center/baseline/layout-center/reset), global scan, baseline alignment, center-in-cells, export ZIP.
- Aggiunta `AnimationStudioView` con sezioni: preview e laboratorio, frame workbench (barra attiva dinamica), azioni rapide (snap checkbox + centra/piedi/ripristina tutti), frame selezionato, export ZIP, expander strumenti avanzati (padding, normalizzazione globale, centra nelle celle, pivot X/Y).
- Aggiunto `AnimationState` record (5 campi): `SnapToGrid`, `ExtractPadding`, `NormalizePolicyIndex`, `PivotX`, `PivotY`.
- Rimosso il tab legacy `Animation` da `MainWindow.axaml`; rimosso il box "Laboratorio" dalla sidebar.
- Aggiunto `AnimationStudioPanel` con `IsVisible` gestito da `ActivateStudio(StudioKind.Animation)`.
- Aggiunto `_animationState` in `MainWindow.axaml.cs` per evitare letture dirette dai controlli XAML rimossi.
- Refactor dei metodi Animation principali per leggere da `_animationState`: `RunBaselineAlignment`, `RunCenterInCells`, `EnterFrameAlignMode`, `AlignAllFramesCenter`, `AlignSelectedFrame`.
- Pivot export (`ExportPngAsync`, `ExportJsonAsync`) ora legge da `_animationState.PivotX/Y` invece di `SliderPivotX/Y.Value` (controlli rimossi).
- `SetWorkbenchActive(bool)` e `SetSelectedFrameInfo(string)` e `SetGlobalScanResult(string)` esposti come API pubblica del pannello per aggiornare lo stato UI dal MainWindow.
- `UpdatePivotLabels()` reso no-op statico: le label sono ora in `AnimationStudioView`.

**Build post-migrazione:** 0 errori, 0 warning.
