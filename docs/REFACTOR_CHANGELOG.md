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

---

## Gomma — refactor contratto + accessibilità UI (2026-05-02)

### `EraserStrokeEventArgs.cs`

- `Size` rinominato in `SideLength` — semantica esatta: lato del quadrato, non raggio.
- Rimosso `Radius => Size` — dead code con semantica errata (restituiva il lato intero spacciandolo per raggio). Nessun consumatore nel codebase.
- Aggiunto `Math.Max(1, sideLength)` nel costruttore — contratto ≥ 1 garantito alla sorgente.
- Doc XML completi per `ImageX`, `ImageY`, `SideLength` e classe.

### `EditorSurface.cs`

- `EraserRadiusProperty` / `EraserRadius` → `EraserSizeProperty` / `EraserSize`.
- Default cambiato da `4` a `1` — per pixel art il default naturale è il singolo pixel.
- Clamp `[1, 64]` al posto del solo `Math.Max(1, ...)` — range esplicitato nella property.
- **Ctrl + rotella del mouse** sul canvas in modalità gomma ridimensiona il quadrato gomma; la rotella senza Ctrl continua a fare zoom.

### `MainWindow.axaml`

- Sostituito il singolo `NumericUpDown` con `EraserSizePanel` (`StackPanel`) contenente:
  - Label "Dim." + `TxtEraserSize` (52 px, valore default 1) + label "px".
  - Pulsanti preset **[1] [2] [4] [8]** con tooltip `N×N px` per cambio rapido dimensione.
  - Tooltip `TxtEraserSize` documenta il Ctrl+rotella.

### `MainWindow.axaml.cs`

- `TxtEraserRadius` → `TxtEraserSize`, `Editor.EraserRadius` → `Editor.EraserSize`.
- `e.Size` → `e.SideLength`; commento chiarisce che il contratto ≥ 1 è garantito dall'EventArgs.
- `EraserSizePanel.IsEnabled` gestisce l'intero pannello (input + preset) al toggle gomma.
- `Editor.PropertyChanged` listener sincronizza `TxtEraserSize` quando Ctrl+rotella cambia `EraserSize` sull'EditorSurface (senza loop).
- 4 handler `BtnEraserSize*.Click` per i preset rapidi.

**Build post-refactor:** 0 errori, 0 warning.
**Publish:** `publish/win-x64`, `dist/win-x64` — OK.

---

## Rimozione sfondo — fix contagocce + feedback + tolleranza Oklab (2026-05-02)

Tre root cause identificate e risolte: la pipetta non raggiungeva mai `RunBackgroundIsolation`, il feedback era ambiguo, e la formula di tolleranza Oklab era sistematicamente troppo stretta.

### Fix 1 — Pipetta non collegata alla rimozione sfondo

**Problema:** `ActivatePipette(int targetIndex)` era dead code — chiamata mai emessa da nessuna parte. Il pulsante "Rimuovi Sfondo" leggeva il colore da `_backgroundIsolationHex`, ma non c'era modo UI di popolare quel campo con il colore catturato dal contagocce.

**Soluzione:**
- Aggiunto `ActivatePipetteForBackground` a `SpriteStudioAction.cs`.
- Aggiunto pulsante `BtnSpriteBgPipette` (◉, 22×22) inline nella riga colore sfondo di `SpriteStudioView.axaml` — visualmente adiacente al campo hex che popola.
- `SpriteStudioView.axaml.cs` emette `ActivatePipetteForBackground` al click.
- `MainWindow.axaml.cs` aggiunge case nel dispatcher `OnSpriteStudioActionRequested`: `ActivatePipette(0)`.

### Fix 2 — Feedback ambiguo

**Problema:** `RunBackgroundIsolation` chiamava `ApplyInPlace` (che restituiva `void`) e mostrava sempre "Sfondo rimosso" — anche se 0 pixel erano stati effettivamente rimossi.

**Soluzione:**
- `BackgroundIsolation.ApplyInPlace` ora restituisce `int` (conteggio pixel rimossi).
- `RunBackgroundIsolation` distingue: `removed > 0` → messaggio di successo con conteggio e parametri; `removed == 0` → messaggio diagnostico che invita a ricontrollare colore e tolleranza.
- Pipetta disattivata automaticamente dopo l'operazione (`ChkPipette.IsChecked = false`).

### Fix 3 — Tolleranza Oklab calibrata adattativamente

**Problema:** La formula precedente `oklabTolSq = (rgbTol / 255.0)²` era derivata dall'intervallo lineare sRGB, non dalla geometria Oklab effettiva. Per colori scuri o chiari, la distanza Oklab reale per ±T shift in RGB è molto diversa da `T/255`, rendendo la tolleranza sistematicamente troppo stretta.

**Soluzione:** `BackgroundIsolation.ApplyInPlace` ora calibra `oklabTolSq` calcolando la distanza Oklab reale:
- Costruisce due colori campione: `key ± tolInt` su tutti i canali (clampato `[0,255]`).
- `oklabTolSq = max(DistanceSquared(key, plus), DistanceSquared(key, minus))`.
- Fallback al metodo lineare solo se `oklabTolSq ≤ 0` (tolleranza 0 o colori identici).

**File modificati:**
- `src/AiPixelScaler.Core/Pipeline/Imaging/BackgroundIsolation.cs`
- `src/AiPixelScaler.Desktop/Views/Studios/SpriteStudioAction.cs`
- `src/AiPixelScaler.Desktop/Views/Studios/SpriteStudioView.axaml`
- `src/AiPixelScaler.Desktop/Views/Studios/SpriteStudioView.axaml.cs`
- `src/AiPixelScaler.Desktop/Views/MainWindow.axaml.cs`

**Build post-fix:** 0 errori, 0 warning.

---

## Rimozione sfondo — fix flood multi-run (2026-05-02)

### Problema: secondo run sempre 0 pixel rimossi

Dopo il primo "Rimuovi sfondo", i pixel di bordo immagine diventano trasparenti (A=0).
Alla seconda esecuzione `ToFlatArray` legge l'immagine aggiornata: tutti i pixel di bordo hanno A=0.
`CanFlood` richiedeva `A != 0` → nessun seed → coda vuota → 0 pixel rimossi.

Doppio blocco Sobel: `BuildSobelEdgeMap` calcolava luma=0 per i pixel trasparenti.
La discontinuità trasparente↔opaco produceva gradienti Sobel elevati → `edge[i]=true` per i pixel
adiacenti all'area già azzerata → flood bloccato anche correggendo solo `CanFlood`.

### Soluzione (3 modifiche in `BackgroundIsolation.cs`)

**1. `CanFlood` — pixel trasparenti passabili:**
Il flood può attraversare pixel già azzerati per raggiungere nuove aree sfondo.

**2. `BuildSobelEdgeMap` — pixel trasparenti esclusi dalla mappa bordi:**
Impedisce che la frontiera trasparente↔sprite generi false edges che bloccano il flood.

**3. Loop di rimozione — conta solo pixel originalmente opachi:**
Evita conteggi gonfiati includendo pixel già trasparenti da run precedenti.

**File modificati:** `src/AiPixelScaler.Core/Pipeline/Imaging/BackgroundIsolation.cs`

**Build post-fix:** 0 errori, 0 warning.
**Publish:** `publish/win-x64`, `dist/win-x64` — OK.
