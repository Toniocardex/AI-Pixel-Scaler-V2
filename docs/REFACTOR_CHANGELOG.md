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

---

## Rimozione sfondo — auto-snap colore dopo Quantize (2026-05-02)

### Problema: Quantize rimappa i colori → pipetta obsoleta → 0 pixel rimossi

Scenario: utente campiona con pipetta `#C1A790` → applica Quantize 32 colori → i pixel di bordo vengono rimappati a `#BE9F87` (palette entry più vicina) → "Rimuovi sfondo" cerca `#C1A790` con tolleranza 15 → distanza > soglia → 0 pixel rimossi.

Il conflitto era intermittente perché dipende da quanto il quantizzatore sposta il colore sfondo rispetto alla tolleranza impostata.

### Soluzione

**`BackgroundIsolation.SnapKeyToBorderColor`** (nuovo metodo pubblico in Core):
- Campiona tutti i pixel opachi sul perimetro dell'immagine.
- Trova il pixel con distanza RGB² minima dal colore chiave.
- Se la distanza è entro `maxRgbDistancePerChannel=40` per canale, restituisce quel pixel come colore corretto; altrimenti restituisce il colore originale invariato.

**`RunBackgroundIsolation`** in `MainWindow.axaml.cs`:
- Chiama `SnapKeyToBorderColor` prima del flood.
- Se snap attivo: aggiorna `_backgroundIsolationHex` + `SyncSpriteCleanupStateFromControls()` → il TextBox nel pannello mostra il colore effettivamente usato.
- Il messaggio di stato include `[colore corretto da #ORIG dopo quantize]` quando lo snap ha operato.

**File modificati:**
- `src/AiPixelScaler.Core/Pipeline/Imaging/BackgroundIsolation.cs`
- `src/AiPixelScaler.Desktop/Views/MainWindow.axaml.cs`

**Build post-fix:** 0 errori, 0 warning.
**Publish:** `publish/win-x64`, `dist/win-x64` — OK.

---

## Audit critici — fix priorità (2026-05-03)

Audit completo del codebase ha identificato e risolto 6 bug critici e 2 fix UX.

### Fix 1 — `_cleanApplied` mai impostato dai filtri individuali

**Problema:** Solo `ExecutePipeline` impostava `_cleanApplied = true`. I filtri individuali (Defringe, Denoise, Median, BackgroundIsolation, AI Cleanup, Quantize) non lo facevano mai. Conseguenza: il workflow guidance rimaneva bloccato al passo 2 ("Applica pulizia") anche dopo aver applicato filtri manualmente.

**Soluzione:** Aggiunto `_cleanApplied = true; UpdateWorkspaceGuidance();` in:
- `RunDefringe()`
- `RunMedianFilter()` (convertito da expression a block body)
- `RunDenoise()` (dopo `RunTransform`)
- `RunBackgroundIsolation()` (quando `removed > 0`)
- `RunAiCleanupWizard()` (dopo `ExecutePipeline`)
- `RunSpriteQuantize()` (dopo `PaletteMapper.ApplyInPlace`)

### Fix 2 — `UpdatePipelinePresetBadge` era uno stub vuoto

**Problema:** Il metodo `UpdatePipelinePresetBadge()` aveva corpo vuoto `{ }`. I preset (Default/Sicuro/Aggressivo) non erano mai indicati visivamente nel pannello.

**Soluzione:**
- Aggiunta UI badge in `SpriteStudioView.axaml`: Grid con `PresetBadgeBorder` e `TxtPresetBadge` affiancati al titolo "Filtri Sprite".
- Aggiunto `SetPresetBadge(string? presetName)` in `SpriteStudioView.axaml.cs`.
- Implementato `UpdatePipelinePresetBadge()` in `MainWindow.axaml.cs`: mostra "Default" / "Sicuro" / "Aggressivo" o nasconde il badge (`null`) quando preset è `None`.
- Aggiunto `ResetPresetOnManualPipelineEdit()` — chiamato da ogni filtro individuale per portare il preset a `None` e nascondere la badge quando l'utente personalizza i parametri.

### Fix 3 — Messaggio Defringe fuorviante

**Problema:** Quando non ci sono pixel semi-trasparenti, il messaggio diceva "Il filtro è no-op su immagini completamente opache" senza indicare cosa fare.

**Soluzione:** Messaggio aggiornato: "Esegui prima 'Rimuovi sfondo' per generare i bordi semi-trasparenti, poi applica Defringe."

### Fix 4 — Messaggio obsoleto "tab 3-4" in `RunImportFramesAsync`

**Problema:** Dopo l'import frame il messaggio suggeriva "Usa i tab 3-4 per rifinire" — riferimento ai tab legacy rimossi nella migrazione Sprite Studio.

**Soluzione:** Messaggio aggiornato a "Usa Tileset Studio per rifinire la griglia, poi Animation Studio per la preview."

**File modificati:**
- `src/AiPixelScaler.Desktop/Views/MainWindow.axaml.cs`
- `src/AiPixelScaler.Desktop/Views/Studios/SpriteStudioView.axaml`
- `src/AiPixelScaler.Desktop/Views/Studios/SpriteStudioView.axaml.cs`

**Build post-fix:** 0 errori, 0 warning.
**Publish:** `dist/win-x64` — OK. `publish/win-x64` — richiede chiusura app (exe in uso).

---

## Rimozione sfondo — rework completo pipeline (2026-05-03)

Analisi sistematica di tutte le root cause dell'inaffidabilità della rimozione sfondo.
8 bug identificati e risolti, con aggiornamento algoritmo core, UI e messaggi diagnostici.

### Root cause 1 — Pipetta campionava 1 pixel solo

**Problema:** `_document[e.X, e.Y]` leggeva esattamente 1 pixel. Bastava cliccare su un pixel JPEG compresso, anti-aliased o con variazione minima rispetto al colore dominante per ottenere un colore errato, rendendo tutto il flood fail.

**Soluzione:** `OnEditorImagePixelPicked` ora chiama `BackgroundIsolation.SampleRegionColor(_document, e.X, e.Y, radius: 2)` — campiona l'area 5×5 attorno al click e restituisce il colore più frequente tra i pixel opachi (alpha ≥ 128). Eliminato il problema di campionamento su pixel devianti.

### Root cause 2 — Edge threshold Sobel default 48 troppo basso

**Problema:** Artefatti JPEG all'interno dello sfondo producono gradienti Sobel di 20-80. Con soglia 48, questi artefatti venivano marcati come "bordo forte" e bloccavano il flood fill su aree che dovevano essere rimosse. Lo sfondo appariva "bucato" in zone casuali.

**Soluzione:** Soglia default portata da 48 a 100 in `Options.EdgeThreshold`, nel campo `_backgroundIsolationEdgeThreshold` (default "100") e nel TextBox UI `TxtSpriteBackgroundEdge`. Artefatti JPEG (gradiente <80) passano, bordi sprite reali (gradiente 200-1440) vengono bloccati. ToolTip aggiornato con guida pratica.

### Root cause 3 — Solo 4-connectivity (nessun diagonale)

**Problema:** Il flood fill propagava solo in 4 direzioni (N/S/E/O). Angoli di sfondo raggiungibili solo in diagonale non venivano rimossi, lasciando pixel isolati agli angoli dello sprite.

**Soluzione:** Aggiunto `Use8Connectivity = true` (default) a `Options`. La propagazione ora include i 4 vicini diagonali, eliminando gli angoli isolati.

### Root cause 4 — Calibrazione Oklab asimmetrica

**Problema:** La calibrazione campionava solo `key ± tolInt` su TUTTI i canali simultaneamente. Per colori saturi (es. #00FF00), lo shift +tolInt sul canale G clipa a 255, producendo una distanza Oklab molto piccola. La tolleranza effettiva era sistematicamente sottostimata per colori saturati.

**Soluzione:** La calibrazione ora campiona 8 punti: 6 assi allineati (±R, ±G, ±B) + 2 diagonali principali. Prende il massimo tra tutte le distanze Oklab², garantendo che il raggio di tolleranza copra correttamente qualsiasi direzione nello spazio colore.

### Root cause 5 — SnapKeyToBorderColor scansionava solo 1px di bordo

**Problema:** Dopo un primo run di background removal, i pixel del bordo esterno diventano trasparenti (A=0). `SnapKeyToBorderColor` scansionava solo il perimetro esterno e non trovava pixel opachi → snap non operava → colore non corretto.

**Soluzione:** `SnapKeyToBorderColor` ora scansiona `borderDepth=3` pixel di profondità dal perimetro (prima solo 1). Ridotto anche `maxRgbDistancePerChannel` da 40 a 32 per snap più conservativo.

### Root cause 6 — "Binarizza alpha" non applicata in RunBackgroundIsolation

**Problema:** `ChkSpriteAlphaThreshold` e `TxtSpriteAlphaThreshold` esistevano nel pannello ma erano collegati solo al full pipeline (`ExecutePipeline`). L'utente attivava il checkbox aspettandosi un effetto, ma non succedeva nulla.

**Soluzione:** Dopo il flood fill (quando `removed > 0`), se `_pipelineFormState.EnableAlphaThreshold` è attivo, `RunBackgroundIsolation` chiama `AlphaThreshold.ApplyInPlace(_document, alphaThr)`. Lo stato barra riporta "+ alpha binarizzata".

### Root cause 7 — Pipetta non si disattivava dopo il campionamento

**Problema:** La pipetta rimaneva attiva dopo il pick del colore. Navigando sul canvas (click/drag per pan) si rischiava di ri-campionare accidentalmente un pixel diverso, cambiando il colore senza volerlo.

**Soluzione:** `OnEditorImagePixelPicked` ora chiama `ChkPipette.IsChecked = false` dopo il campionamento. Auto-disattivazione immediata; l'utente deve ri-attivarla esplicitamente per un secondo campionamento.

### Root cause 8 — Messaggio di errore non diagnostico

**Problema:** "Nessun pixel rimosso — controlla colore sfondo e tolleranza" non dava informazioni su COSA controllare. L'utente non sapeva come procedere.

**Soluzione:** Messaggio di fallimento ora include suggerimenti contestuali:
- Se `tol < 5`: "Aumenta tolleranza (prova 15-30)"
- Se `edge > 80`: "Riduci protezione bordi (prova 50-0) se il flood è bloccato su sfondo uniforme"
- Sempre: "Usa ◉ per campionare il colore esatto dallo sfondo"

### Nuovo metodo: `BackgroundIsolation.SampleRegionColor`

Metodo pubblico in Core:
```
SampleRegionColor(Image<Rgba32> image, int cx, int cy, int radius = 2)
```
Restituisce il colore più frequente (mode) tra i pixel opachi in un'area `(2r+1)×(2r+1)`.
Uso raccomandato nella pipetta per qualsiasi campionamento colore.

**File modificati:**
- `src/AiPixelScaler.Core/Pipeline/Imaging/BackgroundIsolation.cs`
- `src/AiPixelScaler.Desktop/Views/MainWindow.axaml.cs`
- `src/AiPixelScaler.Desktop/Views/Studios/SpriteStudioView.axaml`

**Build post-fix:** 0 errori, 0 warning.
**Publish:** `publish/win-x64`, `dist/win-x64` — OK.
