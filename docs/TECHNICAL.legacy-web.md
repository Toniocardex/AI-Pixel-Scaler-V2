# AI Pixel-Perfect Scaler — Documentazione tecnica

Questo documento descrive l’architettura, le pipeline di dati, i moduli e il ruolo del codice rispetto all’interfaccia utente. È basato sull’implementazione in `src/`, `src-tauri/`.

---

## 1. Stack e scopo

| Componente | Tecnologia |
|------------|------------|
| UI | React 19, TypeScript, Vite 8, Tailwind 4 |
| Immagini | Canvas 2D, `ImageData` |
| Sprite / timeline | Konva, `react-konva` |
| Stato sessione (Sprite Studio) | Zustand (`spriteProjectStore`) |
| Shell desktop | Tauri 2 (WebView) |
| Salvataggio file (desktop) | `@tauri-apps/plugin-dialog` + `plugin-fs` |
| BFS bordo (sfondo) | Web Worker + fallback main thread (`edgeDualBfs` / `edgeDualBfsAsync`) |

**Scopo complessivo:** strumento per **pixel art e asset 2D**: campionamento/ridimensionamento controllato, **chroma key**, **quantizzazione**, **outline**, **tileset / spritesheet**, **texture seamless**, **griglia con pivot (composer)**, e **Sprite Studio** per **sequenze di frame** con **rimozione sfondo a propagazione (BFS)**, **pivot**, strumenti su canvas, **export PNG + JSON** (Tauri o download browser).

L’elaborazione pixel avviene **nel processo del WebView** (JavaScript). Il **Rust** in `src-tauri` non contiene algoritmi di immagine: inizializza l’applicazione, plugin `dialog` / `fs` / `log` (debug).

---

## 2. Comandi e integrazione Tauri

Non esistono “comandi” CLI interni all’eseguibile. L’interazione avviene tramite la UI. L’unico collegamento “di sistema” strutturato è il salvataggio file.

### 2.1 `src/lib/tauriSave.ts`

- `isTauri()` — rileva `__TAURI_INTERNALS__` in `window`.
- `savePng(canvas, defaultName)` — `canvas.toBlob` → in Tauri apre *Salva con nome* e scrive il file; altrimenti download browser.
- `saveJson(content, defaultName)` — stesso pattern per `.json` / `.tsj`.
- `saveZip` (se usato) — stesso per archivi.

Implementazione Tauri: import dinamico di `plugin-dialog` (`save`) e `plugin-fs` (`writeFile`). Annulla → `null`.

### 2.2 `src-tauri/src/lib.rs`

- `tauri::Builder` con `tauri_plugin_dialog::init()` e `tauri_plugin_fs::init()`.
- In build **dev**: opzionalmente `tauri-plugin-log` e apertura **DevTools** su una finestra webview.

Non sono definiti comandi Tauri custom (`#[tauri::command]`) oltre a quanto fornisce il framework: la logica applicativa resta in JS/TS.

---

## 3. Entry point e shell UI

- `src/main.tsx` — `createRoot` + `App`.
- `src/App.tsx` — **unico componente principale** molto ampio: contiene le **5 modalità** (tab) e tutta la logica per pixel, seamless, tileset, composer, navigazione e caricamento **lazy** dello Sprite Studio.

Stato: `appMode: 'pixel' | 'seamless' | 'tileset' | 'composer' | 'sprite-studio'`.

```text
appMode
  → condiziona quali pannelli/canvas e handler sono montati
  → 'sprite-studio' → lazy: import('./components/spriteStudio/SpriteStudioView')
```

Non c’è un router: è **switching condizionale** su `appMode` nel JSX.

---

## 4. Le cinque modalità (cosa fanno, come sono guidate)

### 4.1 Pixel (`pixel` — “Pixel-Perfect Scaler”)

- **Dati:** `sourceRef: ImageData | null` + state (target, offset, colore key, flag chroma/quantize/outline, export, ecc.).
- **Flusso tipico:** caricamento file → `performSampling` verso `target` con offset `offsetX`/`offsetY` → `runPostSamplingPipeline` sull’`ImageData` target.

**`src/lib/imagePipeline.ts` — ordine fisso (dopo campionamento):**

1. Chroma key (se abilitato) — `applyChromaKey` + `parseHexColor`.
2. Quantizzazione canali (se abilitata).
3. Outline 1px (se abilitata) — può sostituire l’`ImageData` (nuovo buffer).

**Comandi UI (concettuali):** file input, slider, toggle pipeline, **pipette** su canvas (`chromaPipette` + `sampleScreenColor` / `eventToImageCoordsInt`), export (asset, canvas) tramite `runExportAsset` o canvas dedicato.

**Undo:** `undoStackRef` (max 20 snapshot: `ImageData` + parametri griglia), `skipNextAutoDetectRef` per evitare doppi auto-detect.

### 4.2 Seamless (`seamless`)

- `src/lib/seamlessTexture.ts` — generazione di **texture senza cuciture**; opzioni (patch, pass, dimensione output, chroma opzionale).
- Download tramite stesso schema `tauriSave` o browser.
- Stato locale: dimensioni tile, URL tile, controlli seamless.

### 4.3 Tileset (`tileset`)

- `src/lib/tilesetSlicer.ts` (e correlati) — lavora sulla **sorgente** `ImageData` in `sourceRef` + griglia (dimensioni celle, padding, offset, ancore).
- Funzionalità rappresentative: `autoDetectTilesetGrid`, `renderTilesetPreview`, `buildPackedTileset`, `buildAtlasJson*`, `buildTiledJson`, `exportFramesAsZip`, `floodFillCropByColor`, `cropImageToGrid`, `extractStripCanvas`, `downloadStripPng`, …
- **Animazione strip:** `frameIndex`, `nudgeByFrame: Record<index, {x,y}>` (limiti e.g. `NUDGE_LIM`).

**Comandi:** trascinamenti su anteprime, controlli numerici griglia, azioni “ritaglia / esporta / flood crop”, animazione con play.

### 4.4 Composer (`composer`)

- `src/lib/tileComposer.ts` — griglia manuale di celle, immagini per cella, **pivot** (presets `PIVOT_PRESETS`, layout `PIVOT_GRID_LAYOUT`), `renderComposerCanvas` (bordi, indici, croce pivot), `buildComposerAtlas` / `buildComposerJsonHash` / `buildComposerJsonTiled`, `saveComposerPng` / `saveComposerJson`.
- Stato: `ComposerGrid`, indice selezionato, mode di adattamento immagine in cella.

**Comandi:** click celle, incolla/carica, ridimensiona griglia, export PNG+JSON.

### 4.5 Sprite Studio (`sprite-studio`)

- **File:** `src/components/spriteStudio/SpriteStudioView.tsx` (caricato con `React.lazy`).
- **Stato globale frame:** `src/store/spriteProjectStore.ts` (Zustand).
- **Modelli:** `src/lib/projectTypes.ts` — `Frame` (id, `fileBlob`, `processedDataUrl`, `crop`, `delta`, `pivot`, `pivotValid`, `duration`, `isMuted`), layout export, `MAX_BFS_PIXELS`, ecc.

**Pipeline ingest / ricalcolo (riassunto):**

1. Da blob: `ImageData` → (opzionale) soglia dimensione → ROI modale o ingest diretto.
2. `edgeDualBfsRemoveAsync` (worker se disponibile) — rimozione “smart” dello sfondo partendo **dai bordi**; tolleranza `bgTolerance`.
3. Opzionale: `keepLargestOpaqueIsland` (CCL) se `cclEnabled`.
4. Geometria: `getOpaqueBounds` + `computeDeltaFromPivotAndBounds` / `geometryFromProcessedImage` / `geometryAfterRectCrop` (dopo crop ROI).

**Mappe in memoria (store, ambito modulo, non requisiti di product):** `imageDataById` per `ImageData` per id frame, `objectUrlById` per URL di anteprima dove usato.

**Strumenti canvas (state locale in view):** es. `select`, `align`, `marquee`, `pivot`, `multikey`, `eraser` — handler mouse/tastiera Konva, chiamate a `setPivot`, `applyRoiCropToFrame`, `nudgeFramePixels`, `applyEraserStroke`, `replaceFrameWithImage`, `reprocessFrame`, …

**Play timeline:** `playing`, `playIndex`, `globalFPS` — scelta `displayUrl` / frame corrente per il canvas.

**Anteprima BFS leggera (sprite view):** stato locale `liveBgUrl` + `makeDownscaledBackgroundPreviewUrl` (debounce) `activeSourceBlob` (tipicamente `fileBlob` del frame), **non** sostituisce il `Frame` in store fino a ricalcolo esplicito.

**Export pack:** `exportPack` in store → `buildSpriteAtlas` (`spriteExport.ts`) + `savePng` + `saveJson` (`tauriSave`).

Documentazione puntuale strumenti UI (onion, allinea, gomma) resta come commenti e copy nel componente; la struttura dati è quella di `Frame` + `imageDataById`.

---

## 5. Elaborazione immagine: file chiave (lib)

| File / area | Ruolo |
|-------------|--------|
| `imagePipeline.ts` | Ordine post-campionamento: chroma → quantize → outline. |
| `applyChromaKey.ts` | Sostituzione colore con alpha. |
| `quantizeColors.ts` | Posterizzazione canali. |
| `applyOutline.ts` | Bordo 1px. |
| `performSampling.ts` | Campionamento griglia sorgente → target. |
| `edgeDualBfs.ts` / `edgeDualBfsCore.ts` | BFS doppia frontiera da bordi. |
| `edgeDualBfsAsync.ts` | Worker o fallback. |
| `edgeDualBfs.worker.ts` | Worker: riceve buffer, ritorna `ImageData` o `emptyContent`. |
| `cclIslands.ts` | CCL, “mantieni isola opaca maggiore”. |
| `pivotGrid.ts` | BBox opaco, delta, crop, pivot normalizzato in bbox. |
| `exportAsset.ts` | Export assemblato in modalità pixel. |
| `tilesetSlicer.ts` | Griglia, atlas, Tiled, zip frame, … |
| `tileComposer.ts` | Composer, atlas, export JSON. |
| `seamlessTexture.ts` | Tileable. |
| `spriteExport.ts` | Atlas JSON + canvas per pacchetto animazione. |
| `spritesheetUtils.ts` | Utilità rettangoli, `nudgeImageData` (stesso `ImageData` W×H). |
| `spritePixelTools.ts` | Gomma, rimozione colore su frame, … |

---

## 6. Persistenza

- **Sessione corrente:** frame e `ImageData` vivono in **Zustand** e nelle `ref` di `App` / `imageDataById` nello store; **nessun** salvataggio automatico su disco di un “progetto”.
- **Persistenza esplicita:** solo quando l’utente avvia un **export** (PNG, JSON, ZIP) attraverso `tauriSave` o download browser.
- I **file di input** restano rappresentati come `Blob` / `fileBlob` per frame; il buffer **processato** è in `ImageData` + `processedDataUrl` (data URL o object URL) per l’anteprima.

---

## 7. Mappa concettuale “azione utente → codice”

| Azione | Effetto principale |
|--------|---------------------|
| Cambio tab | `setAppMode` — monta/smonta sezioni; Sprite Studio in lazy + `Suspense`. |
| Carica immagine (pixel / tileset) | Lettura in `ImageData` / `blob` + `sourceTick` / aggiornamento store. |
| Salva / Esporta | `savePng` / `saveJson` / `saveZip` o equivalenti canvas→blob. |
| Ricalcola BFS (sprite) | `reprocessFrame` / `reprocessAllFrames` — legge `fileBlob` del frame, BFS, aggiorna `imageDataById` e campi `Frame` da `geometryFromProcessedImage`. |
| ROI / crop rettangolare (sprite) | `applyRoiCropToFrame` o `ingestCroppedFromRoi` dopo `ManualCropModal` — nuovo `ImageData`, `fileBlob` aggiornato dove applicabile, pivot mappato con `geometryAfterRectCrop` se valido. |
| Conferma allineamento (nudge) | `nudgeFramePixels` — `nudgeImageData` + aggiornamento pivot traslato in bbox opaca (`applyProcessedBuffer`). |
| Gomma / color key / patch | `commitPatchedImage` o `applyProcessedBuffer` con geometria ricalcolata. |

---

## 8. Limiti e note di design

- **`App.tsx`** concentra molte responsabilità (così come lo store e la view Sprite Studio crescono con le feature). Refactor futuro possibile: spezzare per `appMode` o “feature folder”.
- **Warning Vite** noto: `applyChromaKey` importata sia staticamente che dinamicamente in alcuni flussi; non blocca la build.
- **Reprocess (sprite):** `fileBlob` resta la **sorgente pre-BFS** (import o post-crop) da cui rieseguire BFS+opzioni; **non** viene sostituito con l’output processato, per evitare doppia applicazione dello stesso algoritmo su immagine già scontornata.
- **Browser:** senza Tauri, il salvataggio è download; la logica di processing resta invariata.

---

## 9. Build e pacchettizzazione

- `npm run build` — `tsc -b && vite build` — output in `dist/`.
- `npm run tauri build` (o `npx tauri build`) — esegue `beforeBuildCommand` (stesso `npm run build`), compila **Rust** `src-tauri`, genera `app.exe` e installer (es. NSIS, MSI) secondo `src-tauri/tauri.conf.json`.

---

*Generato in linea con l’albero sorgente del repository; in caso di divergenze, la fonte autorevole resta il codice.*
