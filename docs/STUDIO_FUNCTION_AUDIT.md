# Studio Function Audit

Stato: audit approvato come guida di refactor; implementazione progressiva in corso.

Regola: nessuna rimozione e' stata applicata. Le decisioni `remove` o `merge` sono solo candidate e richiedono conferma.

Nota implementativa corrente: **Sprite Studio chiuso al 100%** (2026-05-02). **Tileset Studio chiuso al 100%** (2026-05-02). **Animation Studio chiuso al 100%** (2026-05-02). **Video Frame Extractor MVP implementato** (2026-05-03): pulsante «Da video…» in Animation Studio; `FFmpegLocator` (strategia 3 passi: PATH → auto-install → manuale), `FFmpegDownloader` (download automatico da gyan.dev con ProgressBar + annullamento), `FfmpegSetupDialog` (dialog unificato auto+manuale, sostituisce FfmpegConfigDialog), `VideoFrameExtractor`, `VideoImportDialog`; `UiPreferencesService` esteso con `FfmpegFolder`. **Sprite Studio «Nuovo» canvas** (2026-05-03): pulsante «Nuovo» apre `NewCanvasDialog` — dimensioni 1–8192 px, sfondo sceglibile tra Trasparente / Bianco / Nero / Magenta / Verde / Blu. **EditorSurface — bordo documento e griglia universale** (2026-05-03): `ShowDocumentBorder` (default `true`) disegna bordo arancione attorno al documento; `DrawLightGrid` usa doppio passaggio chiaro+scuro visibile su qualsiasi sfondo. **GridSectionView — sezione griglia condivisa** (2026-05-03): estratta da TilesetStudioView, incorporata in entrambi Tileset e Animation Studio. Contiene griglia (W×H, cols×rows, preset), pivot 3×3, riferimenti visivi, genera griglia, esporta guida PNG e importa frame (handler unificato `RunImportFramesAsync`). `GridSectionAction` enum, `GridState` record, sincronizzazione bidirezionale con `_tilesetState` tramite `ApplyGridStateFromSection` e `GridStateFromTilesetState`. Start Page e routing shell sono stati introdotti. `SpriteStudioView` copre «Nuovo», import, cleanup, ROI, slicing, atlas pulito, resize/mirror ed export PNG/JSON. `TilesetStudioView` espone palette/stilizza, seamless/tile preview, pad-to-multiple, crop/POT, snap celle ed export Tiled (griglia estratta in GridSectionView). `AnimationStudioView` espone preview animazione, sandbox, frame workbench (entra/applica/annulla), azioni rapide allineamento, frame selezionato, normalizzazione globale, centra nelle celle, pivot engine, export ZIP frame e import da video (griglia tramite GridSectionView). I tab legacy `Sprite`, `Slice`, `Selezione`, `Tileset` e `Animation` sono stati rimossi; ogni Studio ha un pannello dedicato attivato da `ActivateStudio(StudioKind)`.

| Funzione | UI attuale | Handler / modulo | Dipendenze principali | Studio destinazione | Decisione proposta | Motivazione |
|---|---|---|---|---|---|---|
| Apri immagine | Menu, toolbar, Sprite tab, empty state | `OpenImageAsync` | `_document`, `_backup`, workspace tabs | Sprite | keep + merge UI | Entry point essenziale; mantenere toolbar/menu, evitare duplicati nel pannello. |
| Ripristina originale | Menu, toolbar | `RevertToBackup` | `_backup`, undo, floating paste | Sprite | keep | Utile in ogni flusso immagine, resta comando shell. |
| Undo | Menu, toolbar | `TryUndo` / `WorkspaceUndoCoordinator` | snapshot workspace | Shell | keep | Cross-studio, non appartiene a un singolo Studio. |
| Copia/Incolla appunti | Menu, Sprite tab | `SelectionController`, `FloatingPasteCoordinator` | clipboard, ROI, floating overlay | Sprite | keep | Serve per composizione sprite e floating paste. |
| Rileva sprite automatico | Toolbar | `SlicingController.RunCcl` | `_cells`, `CclAutoSlicer` | Sprite | keep | Isolamento contorni/sprite statici. |
| Selezione ROI toolbar | Toolbar flyout | `ToggleToolbarSelectionMode`, `CropToSelection`, `RemoveSelectedArea` | `_activeSelectionBox`, editor selection | Sprite | keep | ROI non distruttiva e modifica sprite. |
| Griglia canvas | Toolbar | `Editor.ShowGrid`, `WorldGridSize` | `EditorSurface` | Shell | keep | Strumento di visualizzazione comune. |
| Snap magnetico | Toolbar | `Editor.SnapToGrid`, `SnapGridSize` | griglia, selection, editor | Shell | keep | Comune a Sprite e Tileset. |
| Centra canvas | Toolbar | `RunCenterCanvas` | viewport editor | Shell | keep | Navigazione viewport comune. |
| Gomma | Toolbar | `OnEraserStroke` | `_document`, undo, editor region update | Sprite | keep | Editing pixel/sprite statico. |
| Matita Ripristino | Toolbar | `OnRestorePencilStroke` | colore pipetta, `_document`, undo, editor region update | Sprite | keep | Ripristina solo pixel trasparenti (`A == 0`) con il colore campionato; brush condiviso con gomma, default 1 px. |
| Contagocce | Toolbar + picker inline | `UpdatePipetteMode`, `ActivatePipette` | pipeline color inputs | Sprite | keep | Campionamento colore per pulizia/palette. |
| Workflow rapido | Sprite tab | `RunWorkspaceGuideActionAsync`, `AdvanceWorkflowStepAsync` | workflow state, document, cells | Sprite | keep | Guida il flusso sprite base. |
| Quick process | Pipeline panel | `RunQuickProcess` / `PipelineExecutionService` | `PipelineViewModel`, `_cleanApplied` | Sprite | keep | Pulizia sprite base. |
| Preset default/sicuro/aggressivo | Pipeline panel | `ApplySafePresetToControls`, `ApplyAggressivePresetToControls`, futura azione default | `PipelineViewModel` | Sprite | keep + redefine | I preset non sono filtri: diventano ricette che impostano valori ideali sui filtri visibili e poi lasciano l'utente in stato `Personalizzato` se modifica i parametri. |
| Pipeline avanzata | Pipeline panel | `RunPixelPipeline` | rimozione sfondo, edge refinement, denoise, quantize opzionale, outline | Sprite | keep + merge | Deve diventare azione batch sui filtri visibili, non modulo parallelo. |
| AI cleanup | Pipeline panel | `RunAiCleanupWizard` | alpha, defringe, denoise, palette opzionale | Sprite | keep + merge | Resta one-click cleanup, ma usa `BackgroundIsolation` come unico filtro dedicato di rimozione sfondo. |
| Rimozione sfondo | Sprite Studio cleanup | `RunBackgroundIsolation` | colore sfondo, tolleranza, soglia bordi | Sprite | keep | Unico filtro di rimozione sfondo: flood fill dal bordo, non tocca aree interne non collegate. |
| Defringe | Pipeline panel | `RunDefringe` | alpha/semi-transparent pixels | Sprite | keep | Refinement contorni. |
| Median filter | Pipeline panel | `RunMedianFilter` | document transform | Sprite | keep | Pulizia rumore. |
| Denoise islands | Pipeline panel | `RunDenoise` | island threshold | Sprite | keep | Rimozione pixel isolati. |
| Quantize / Riduci palette | SpriteStudioView | `RunSpriteQuantize` (standalone), `RunPaletteReduce` (Tileset) | max colors, method — da `_pipelineFormState` | Sprite | **done** | `RunSpriteQuantize` legge `MaxColors`/`QuantizerIndex` da stato e chiama direttamente `PaletteExtractorAlgorithms` + `PaletteMapper`; nessun dither in Sprite. `RunPaletteReduce` resta per Tileset con dither e palette preset. |
| Resize nearest | Pipeline panel | `RunNearestResize` | target W/H | Sprite | keep | Pixel scaling semplice. |
| Crop manuale immediato | Slice tab | `ChkSpriteCrop`, `OnEditorImageSelectionCompleted` | editor selection, `AtlasCropper` | Sprite | keep | Diverso dalla ROI non distruttiva; va etichettato chiaramente. |
| Dividi a griglia | Slice tab | `SlicingController.RunGridSlice` | rows/cols, `_cells` | Sprite | keep | Slicing manuale sprite sheet. |
| Atlas pulito / manual paste | Slice tab | paste mode methods | `_pasteSource`, `_pasteBuffer`, cell click | Sprite | keep | Composizione Floating Paste del mockup. |
| Salva frame selezionato | Slice tab | `SlicingController.SaveSelectedFrameAsync` | selected cell, storage | Sprite | keep | Export rapido di un frame. |
| Analisi dimensioni celle | AnimationStudioView | `RunGlobalScan`, `_animationState` | `_cells`, frame stats | Animation | moved | Diagnostica pre-allineamento. |
| Baseline alignment batch | AnimationStudioView | `RunBaselineAlignment`, `_animationState` | `_cells`, document replace | Animation | moved | Anti-jitter/baseline. |
| Frame workbench | AnimationStudioView | `EnterFrameAlignMode`, commit/cancel | `FrameSheet`, editor frame mode | Animation | moved | Workbench attivato da AnimationStudioPanel. |
| Align all center/baseline/reset | AnimationStudioView | align frame methods, `_animationState` | `FrameSheet`, editor frame offsets | Animation | moved | Anti-jitter batch. |
| Align selected frame | AnimationStudioView | selected align/reset methods, `_animationState` | selected frame, snap | Animation | moved | Correzione manuale pivot/frame. |
| Pivot sliders export | AnimationStudioView | `SliderAnimPivotX/Y`, `_animationState` | export metadata/layout | Animation | moved | Pivot animazione e metadata. |
| Preview animazione | AnimationStudioView | `OpenAnimationPreview` | `_cells`, current atlas | Animation | moved | Azione primaria Animation Studio. |
| Sandbox fisica | AnimationStudioView | `OpenSandbox` | current animation/game preview | Animation | moved | Laboratorio dentro Animation Studio. |
| Estrai frame da video | **Implementato** (2026-05-03) — pulsante «Da video…» in Animation Studio | `FFmpegLocator`, `FFmpegDownloader`, `VideoFrameExtractor`, `FfmpegSetupDialog`, `VideoImportDialog` | MP4 H.264, FFmpeg auto-scaricabile o configurabile, output PNG temp, atlas timeline | Animation | **done** | MVP: apri MP4 H.264, metadati da ffprobe, range start/end, FPS target o every-N, estrai PNG in temp, import come atlas nella timeline. FFmpeg cercato in PATH → cartella auto-download → cartella manuale. `FfmpegSetupDialog` offre download automatico (gyan.dev, ProgressBar, annullamento) o percorso manuale. |
| Palette/stilizza | TilesetStudioView | `RunPaletteReduce`, `_tilesetState` | Wu/presets/dither | Tileset | moved | `Stilizza` e' confluito in Tileset Studio; Sprite mantiene solo `Quantize` autonomo nel pannello filtri. |
| Mirror H/V | SpriteStudioView | `RunMirror` | document transform | Sprite | moved | Trasformazione sprite; duplicato rimosso dal blocco Tileset/Stilizza. |
| Tileable seamless | TilesetStudioView | `RunMakeTileable`, `_tilesetState` | `SeamlessEdge`, blend | Tileset | moved | Appartiene al Tileset Studio. |
| Tile preview 3x3 | TilesetStudioView | `TilesetStudioAction.ToggleTilePreview` | editor tile preview mode | Tileset | moved | Preview pattern. |
| Pad to multiple | TilesetStudioView | `RunPadToMultiple`, `_tilesetState` | `AutoPad` | Tileset | moved | Allineamento dimensioni tileset. |
| Export PNG | Sprite/Export tab | `ExportController.ExportPngAsync` | layout builder, cells | Sprite | keep + merge UI | Duplicato reale tra quick export e Export tab. |
| Export JSON | Sprite/Export tab | `ExportController.ExportJsonAsync` | metadata/cells/palette id | Sprite | keep + merge UI | Duplicato reale tra quick export e Export tab. |
| Export ZIP frames | AnimationStudioView + Export tab | `ExportController.ExportFramesZipAsync` | cells, cropped frames | Animation | moved | Comando primario in Animation Studio; tab Export resta scorciatoia condivisa. |
| Export Tiled JSON | TilesetStudioView + Export tab | `ExportController.ExportTiledMapJsonAsync` | cells, tile dimensions | Tileset | keep + shortcut | Export tileset; il comando primario e' nel Tileset Studio, il tab Export resta scorciatoia legacy condivisa. |
| Grid/crop-to-grid preview | TilesetStudioView + align grid shell | `BuildTemplateOptions`, `RunCropToAlignGrid` | grid preview, `GridAlignmentCropper` | Tileset | keep + move | Il template e' migrato in Tileset Studio; il pannello align grid shell resta finche' non sara' completata la migrazione crop-to-grid dedicata. |
| Center in cells | Animation advanced | `RunCenterInCells` | `_cells`, `CellCentering` | Animation | keep | Allineamento frame/celle. |
| Snap cells to grid | Animation advanced | `RunSnapCellsToGrid` | `_cells`, reference grid | Tileset | keep | Allineamento griglia/celle. |
| Crop & POT | TilesetStudioView | `RunCropPipeline`, `_tilesetState` | crop mode, ROI, POT policy | Tileset | keep | Normalizzazione asset/tile. |
| Genera griglia template | **GridSectionView** (condivisa) | `RunGenerateTemplate`, `BuildTemplateOptions`, `OnGridSectionActionRequested` | `GridTemplateGenerator`, cells | **Grid** | **done** — migrata in GridSectionView condivisa tra Tileset e Animation Studio. |
| Esporta guida PNG | **GridSectionView** (condivisa) | `ExportTemplateAsync`, `OnGridSectionActionRequested` | `_templateDocument` | **Grid** | **done** — migrata in GridSectionView. |
| Importa frame | **GridSectionView** (condivisa, handler unificato) | `RunImportFramesAsync`, `OnGridSectionActionRequested` | multi-file PNG, `_tilesetState` | **Grid** | **done** — unico `RunImportFramesAsync`; disponibile da entrambi gli studio tramite GridSectionView. |
| Selezione libera tab | Selezione tab | `SelectAll`, `ClearSelection`, export/crop | `_activeSelectionBox` | Sprite | keep + merge UI | Deve fondersi col pannello Sprite ROI. |
| Nuovo workspace/tab | Top workspace strip | workspace tab methods | `WorkspaceTabsController` | Shell | keep | Gestione documenti cross-studio. |
| Nuovo tab da appunti | Top workspace strip | `AddWorkspaceFromClipboardAsync` | clipboard image | Shell | keep | Cross-studio. |
| Comprimi pannello destro | right panel | `SetPanelCollapsed` | layout columns | Shell | keep | Layout generale. |

## Candidate merge/remove da approvare

| Candidate | Proposta | Motivo |
|---|---|---|
| Cleanup image filters ripetuti | merge in `Rimozione sfondo`, `Edge Refinement`, `Denoise` | BackgroundIsolation/alpha, defringe/outline e median/island/majority erano dispersi tra quick workflow, pipeline e wizard. |
| Quantize dentro cleanup | move standalone filter | La quantizzazione richiede controllo separato: colori, metodo, dither e palette preset non devono sembrare parte obbligatoria della pulizia. |
| Preset pulizia | keep as recipes | I preset restano utili se impostano valori sui filtri visibili; non devono duplicare funzioni o nascondere passaggi. |
| Quick export PNG/JSON + Export tab PNG/JSON | merge UI, keep handler unico | Stesso handler e stesso output, evitare due moduli visuali separati. |
| Laboratorio card + menu Animazione/Sandbox | merge UI in Animation Studio, keep menu shortcut | Stesse funzioni, posizione primaria nel nuovo Studio. |
| Selezione toolbar + Selezione tab | merge UI in Sprite Studio, keep toolbar shortcut | Stesso stato ROI, oggi disperso in due punti. |
| Start page card routing + mini Studio navigator attuale | keep Start page, ridurre navigator a breadcrumb dopo migrazione | Lo Studio sara' scelto all'avvio, il mini navigator diventera' secondario. |
| Sprite Studio view + tab legacy Sprite/Slice/Stilizza/Export/Selezione | Sprite closed | `SpriteStudioView` e' l'ingresso operativo Sprite. I tab legacy `Sprite`, `Slice` e `Selezione` sono rimossi; `Stilizza` e funzioni tile restano da migrare in Tileset Studio. |
| ROI Sprite Studio + tab Selezione legacy | migrated + legacy removed | Il pannello Sprite mostra e controlla la ROI completa; il tab legacy `Selezione` e i suoi controlli XAML/code-behind sono stati rimossi. |
| Slicing Sprite Studio + tab Slice legacy | migrated + legacy removed | Il pannello Sprite mostra e controlla crop manuale, rileva sprite, griglia, frame list, export frame e atlas pulito; il tab legacy `Slice` e i suoi controlli XAML/code-behind sono stati rimossi. |
| Cleanup Sprite Studio + pipeline legacy | migrated + legacy removed | Il pannello Sprite mostra e controlla preset, Rimozione sfondo, Edge Refinement, Denoise e Quantize; lo stato pipeline e' in memoria, non in controlli XAML legacy. |
| Trasformazioni/export Sprite + tab legacy Stilizza/Export | Sprite closed | Il pannello Sprite espone resize nearest, mirror H/V, export PNG e JSON. `Stilizza` passa a Tileset Studio; export ZIP/Tiled restano rispettivamente Animation/Tileset. |
| Video Frame Extractor | **done** — Animation Studio (2026-05-03) | MVP implementato: FFmpegLocator + VideoFrameExtractor + dialog config + dialog import. Non duplicato di import frame: estrae PNG da video e li consegna al flusso frame esistente. |

## Video Frame Extractor — MVP implementato (2026-05-03)

| Ambito | Stato |
|---|---|
| Studio proprietario | `Animation Studio` — pulsante «Da video…» in sezione Export/Import. |
| Scope MVP | ✅ Apri MP4 H.264, metadati da ffprobe (durata/FPS/res), range start/end, FPS target o every-N, PNG in temp, atlas nella timeline. |
| Backend | ✅ `FFmpegLocator`: 3 passi — PATH di sistema, `%LOCALAPPDATA%\AiPixelScaler\ffmpeg` (auto-download), cartella manuale (`ui-preferences.json`). `FFmpegDownloader`: scarica `ffmpeg-release-essentials.zip` da gyan.dev, estrae `ffmpeg.exe`+`ffprobe.exe`. `FfmpegSetupDialog`: dialog unificato con ProgressBar download + sezione percorso manuale. |
| Formati futuri | MOV, WebM, AVI — non in MVP. |
| Fuori MVP | Deduplica, scene detection, crop ROI, cleanup batch, export atlas diretto. |
| Integrazione Sprite | Solo destinazione futura per frame singolo o sequenza breve da pulire/slicare. |
| Nuovi file | `Services/FFmpegLocator.cs`, `Services/FFmpegDownloader.cs`, `Services/VideoFrameExtractor.cs`, `Views/FfmpegSetupDialog.cs`, `Views/VideoImportDialog.cs` |
| File modificati | `Services/UiPreferencesService.cs` (+FfmpegFolder), `Studios/AnimationStudioAction.cs` (+ImportFromVideo), `AnimationStudioView.axaml/.cs`, `MainWindow.axaml.cs` |

## Default filtri Sprite Studio

Questi default sono il punto di partenza ideale. I preset modificano questi valori, ma ogni modifica manuale dell'utente deve marcare lo stato come `Personalizzato`.

| Filtro | Default | Note UI |
|---|---|---|
| Rimozione sfondo | Background color `#00FF00`, tolerance `10`, edge threshold `48`, Alpha threshold `128` | BackgroundIsolation e alpha threshold; nessun secondo modulo di rimozione sfondo. |
| Edge Refinement | Defringe opaque threshold `250`, Outline off, Outline color `#000000` | Contiene defringe e contorno 1px. |
| Denoise | Median manual/off, Island min size `2`, Majority denoise off | Contiene median, island denoise e majority 3x3. |
| Quantize | Max colors `16`, Method `Wu`, Dither off, Palette preset `Auto` | Filtro autonomo con apply dedicato. |
| Resize | Nearest neighbor, target vuoto o dimensione corrente | Non parte automaticamente nei preset di pulizia. |

## Preset approvati come ricette

| Preset | Effetto |
|---|---|
| Default | Ripristina i valori ideali sopra. |
| Sicuro | Pulizia leggera, quantize disattivato, denoise conservativo. |
| Aggressivo | Pulizia forte, denoise piu' marcato, quantize opzionale ma non obbligatorio. |
| One-click cleanup | Esegue i filtri abilitati con i valori correnti; non introduce parametri nascosti. |

## Nessuna rimozione approvata

Al momento non ci sono funzioni marcate `remove`. Le eliminazioni andranno decise dopo la revisione di questa matrice.
