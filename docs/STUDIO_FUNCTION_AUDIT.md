# Studio Function Audit

Stato: audit approvato come guida di refactor; implementazione progressiva in corso.

Regola: nessuna rimozione e' stata applicata. Le decisioni `remove` o `merge` sono solo candidate e richiedono conferma.

Nota implementativa corrente: Start Page e routing shell sono stati introdotti. Nel primo passaggio Sprite Studio i filtri di cleanup sono stati riallineati alla matrice: preset come ricette, Quantize come filtro autonomo, Denoise come gruppo separato da Quantize.

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
| Contagocce | Toolbar + picker inline | `UpdatePipetteMode`, `ActivatePipette` | pipeline color inputs | Sprite | keep | Campionamento colore per pulizia/palette. |
| Workflow rapido | Sprite tab | `RunWorkspaceGuideActionAsync`, `AdvanceWorkflowStepAsync` | workflow state, document, cells | Sprite | keep | Guida il flusso sprite base. |
| Quick process | Pipeline panel | `RunQuickProcess` / `PipelineExecutionService` | `PipelineViewModel`, `_cleanApplied` | Sprite | keep | Pulizia sprite base. |
| Preset default/sicuro/aggressivo | Pipeline panel | `ApplySafePresetToControls`, `ApplyAggressivePresetToControls`, futura azione default | `PipelineViewModel` | Sprite | keep + redefine | I preset non sono filtri: diventano ricette che impostano valori ideali sui filtri visibili e poi lasciano l'utente in stato `Personalizzato` se modifica i parametri. |
| Pipeline avanzata | Pipeline panel | `RunPixelPipeline` | alpha clean, edge refinement, denoise, quantize opzionale, outline | Sprite | keep + merge | Deve diventare azione batch sui filtri visibili, non modulo parallelo. |
| AI cleanup | Pipeline panel | `RunAiCleanupWizard` | edge, alpha, defringe, denoise, palette opzionale | Sprite | keep + merge | Resta one-click cleanup, ma orchestra filtri visibili; non deve duplicare la UI dei filtri. |
| Alpha Clean | Pipeline panel | `RunQuickProcess`, `RunEdgeBackground`, alpha/chroma controls | chroma key, edge background, alpha threshold | Sprite | keep + merge | Accorpa le funzioni ripetute di rimozione sfondo/alpha in un solo gruppo. |
| Defringe | Pipeline panel | `RunDefringe` | alpha/semi-transparent pixels | Sprite | keep | Refinement contorni. |
| Median filter | Pipeline panel | `RunMedianFilter` | document transform | Sprite | keep | Pulizia rumore. |
| Edge background remove | Pipeline panel | `RunEdgeBackground` | key color/tolerance | Sprite | keep | Isolamento sfondo/contorni. |
| Denoise islands | Pipeline panel | `RunDenoise` | island threshold | Sprite | keep | Rimozione pixel isolati. |
| Quantize / Riduci palette | Pipeline/Stilizza tab | `RunPaletteReduce`, quantize controls, palette presets | max colors, method, dither, palette preset | Sprite | keep + move standalone | Deve uscire dalla pulizia immagine e diventare filtro autonomo `Quantize`, con controllo fine dei parametri. |
| Resize nearest | Pipeline panel | `RunNearestResize` | target W/H | Sprite | keep | Pixel scaling semplice. |
| Crop manuale immediato | Slice tab | `ChkSpriteCrop`, `OnEditorImageSelectionCompleted` | editor selection, `AtlasCropper` | Sprite | keep | Diverso dalla ROI non distruttiva; va etichettato chiaramente. |
| Dividi a griglia | Slice tab | `SlicingController.RunGridSlice` | rows/cols, `_cells` | Sprite | keep | Slicing manuale sprite sheet. |
| Atlas pulito / manual paste | Slice tab | paste mode methods | `_pasteSource`, `_pasteBuffer`, cell click | Sprite | keep | Composizione Floating Paste del mockup. |
| Salva frame selezionato | Slice tab | `SlicingController.SaveSelectedFrameAsync` | selected cell, storage | Sprite | keep | Export rapido di un frame. |
| Analisi dimensioni celle | Animation tab advanced | `RunGlobalScan` | `_cells`, frame stats | Animation | keep | Diagnostica pre-allineamento. |
| Baseline alignment batch | Animation tab advanced | `RunBaselineAlignment` | `_cells`, document replace | Animation | keep | Anti-jitter/baseline. |
| Frame workbench | Animation tab | `EnterFrameAlignMode`, commit/cancel | `FrameSheet`, editor frame mode | Animation | keep | Base per mockup Animation Studio. |
| Align all center/baseline/reset | Animation tab | align frame methods | `FrameSheet`, editor frame offsets | Animation | keep | Anti-jitter batch. |
| Align selected frame | Animation tab | selected align/reset methods | selected frame, snap | Animation | keep | Correzione manuale pivot/frame. |
| Pivot sliders export | Animation/Export tab | `SliderPivotX/Y`, `UpdatePivotLabels` | export metadata/layout | Animation | keep | Pivot animazione e metadata. |
| Preview animazione | Laboratorio/menu | `OpenAnimationPreview` | `_cells`, current atlas | Animation | keep + move | Deve diventare azione primaria Animation Studio. |
| Sandbox fisica | Laboratorio/menu | `OpenSandbox` | current animation/game preview | Animation | keep | Laboratorio visibile dentro Animation Studio. |
| Palette/stilizza | Stilizza tab | `RunPaletteReduce`, palette controls | K-Means/presets/dither | Sprite | keep + merge into Quantize | La parte palette/quantizzazione confluisce nel filtro autonomo `Quantize`; eventuale applicazione a tutti i frame va riproposta in Animation Studio. |
| Mirror H/V | Stilizza tab | `RunMirror` | document transform | Sprite | keep | Trasformazione sprite. |
| Tileable seamless | Stilizza tab | `RunMakeTileable` | `SeamlessEdge`, blend | Tileset | keep + move | Appartiene al Tileset Studio. |
| Tile preview 3x3 | Stilizza tab | `ChkTilePreview` | editor tile preview mode | Tileset | keep + move | Preview pattern. |
| Pad to multiple | Stilizza tab | `RunPadToMultiple` | `AutoPad` | Tileset | keep + move | Allineamento dimensioni tileset. |
| Export PNG | Sprite/Export tab | `ExportController.ExportPngAsync` | layout builder, cells | Sprite | keep + merge UI | Duplicato reale tra quick export e Export tab. |
| Export JSON | Sprite/Export tab | `ExportController.ExportJsonAsync` | metadata/cells/palette id | Sprite | keep + merge UI | Duplicato reale tra quick export e Export tab. |
| Export ZIP frames | Export tab | `ExportController.ExportFramesZipAsync` | cells, cropped frames | Animation | keep | Output frame animation. |
| Export Tiled JSON | Export tab | `ExportController.ExportTiledMapJsonAsync` | cells, tile dimensions | Tileset | keep | Export tileset. |
| Grid/crop-to-grid preview | Sprite tab expander | `InitAlignGridPanel`, `RunCropToAlignGrid` | grid preview, `GridAlignmentCropper` | Tileset | keep + move | Corrisponde al mockup Tileset. |
| Center in cells | Animation advanced | `RunCenterInCells` | `_cells`, `CellCentering` | Animation | keep | Allineamento frame/celle. |
| Snap cells to grid | Animation advanced | `RunSnapCellsToGrid` | `_cells`, reference grid | Tileset | keep | Allineamento griglia/celle. |
| Crop & POT | Animation advanced | `RunCropPipeline` | crop mode, ROI, POT policy | Tileset | keep | Normalizzazione asset/tile. |
| Genera griglia template | Tileset tab | `RunGenerateTemplate` | `GridTemplateGenerator`, cells | Tileset | keep | Template grid. |
| Esporta guida PNG | Tileset tab | `ExportTemplateAsync` | `_templateDocument` | Tileset | keep | Export guida. |
| Importa frame | Tileset tab | `RunImportFramesAsync` | multi-file PNG, cell sizing | Animation | keep + move | Visual atlas builder del mockup Animation. |
| Selezione libera tab | Selezione tab | `SelectAll`, `ClearSelection`, export/crop | `_activeSelectionBox` | Sprite | keep + merge UI | Deve fondersi col pannello Sprite ROI. |
| Nuovo workspace/tab | Top workspace strip | workspace tab methods | `WorkspaceTabsController` | Shell | keep | Gestione documenti cross-studio. |
| Nuovo tab da appunti | Top workspace strip | `AddWorkspaceFromClipboardAsync` | clipboard image | Shell | keep | Cross-studio. |
| Comprimi pannello destro | right panel | `SetPanelCollapsed` | layout columns | Shell | keep | Layout generale. |

## Candidate merge/remove da approvare

| Candidate | Proposta | Motivo |
|---|---|---|
| Cleanup image filters ripetuti | merge in `Alpha Clean`, `Edge Refinement`, `Denoise` | Chroma/edge/alpha, defringe/outline e median/island/majority sono oggi dispersi tra quick workflow, pipeline e wizard. |
| Quantize dentro cleanup | move standalone filter | La quantizzazione richiede controllo separato: colori, metodo, dither e palette preset non devono sembrare parte obbligatoria della pulizia. |
| Preset pulizia | keep as recipes | I preset restano utili se impostano valori sui filtri visibili; non devono duplicare funzioni o nascondere passaggi. |
| Quick export PNG/JSON + Export tab PNG/JSON | merge UI, keep handler unico | Stesso handler e stesso output, evitare due moduli visuali separati. |
| Laboratorio card + menu Animazione/Sandbox | merge UI in Animation Studio, keep menu shortcut | Stesse funzioni, posizione primaria nel nuovo Studio. |
| Selezione toolbar + Selezione tab | merge UI in Sprite Studio, keep toolbar shortcut | Stesso stato ROI, oggi disperso in due punti. |
| Start page card routing + mini Studio navigator attuale | keep Start page, ridurre navigator a breadcrumb dopo migrazione | Lo Studio sara' scelto all'avvio, il mini navigator diventera' secondario. |

## Default filtri Sprite Studio

Questi default sono il punto di partenza ideale. I preset modificano questi valori, ma ogni modifica manuale dell'utente deve marcare lo stato come `Personalizzato`.

| Filtro | Default | Note UI |
|---|---|---|
| Alpha Clean | Chroma key off, Edge background tolerance `8`, Alpha threshold `128` | Gruppo unico per rimozione sfondo, chroma e alpha. |
| Edge Refinement | Defringe opaque threshold `250`, Outline off, Outline color `#000000` | Contiene defringe e contorno 1px. |
| Denoise | Median manual/off, Island min size `2`, Majority denoise off | Contiene median, island denoise e majority 3x3. |
| Quantize | Max colors `16`, Method `K-Means OKLab`, Dither off, Palette preset `Auto` | Filtro autonomo con apply dedicato. |
| Resize | Nearest neighbor, target vuoto o dimensione corrente | Non parte automaticamente nei preset di pulizia. |

## Preset approvati come ricette

| Preset | Effetto |
|---|---|
| Default | Ripristina i valori ideali sopra. |
| Sicuro | Pulizia leggera, quantize disattivato, denoise conservativo. |
| Aggressivo + Recupero | Pulizia forte, denoise piu' marcato, quantize opzionale ma non obbligatorio. |
| One-click cleanup | Esegue i filtri abilitati con i valori correnti; non introduce parametri nascosti. |

## Nessuna rimozione approvata

Al momento non ci sono funzioni marcate `remove`. Le eliminazioni andranno decise dopo la revisione di questa matrice.
