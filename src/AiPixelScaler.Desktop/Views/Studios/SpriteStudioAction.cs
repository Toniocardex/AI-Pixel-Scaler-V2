namespace AiPixelScaler.Desktop.Views.Studios;

public enum SpriteStudioAction
{
    CreateBlankCanvas,          // crea un nuovo canvas vuoto con dimensioni personalizzate
    OpenImage,
    ApplyDefaultPreset,
    ApplySafePreset,
    ApplyAggressivePreset,
    RunCleanup,
    RunOneClickCleanup,
    ApplyPipeline,
    ApplyDefringe,
    ApplyMedian,
    ApplyBackgroundIsolation,
    ApplyGlobalChromaKey,           // scansione globale: rimuove isole interne non connesse al bordo
    ApplyIslandCleanup,             // rimuove blob opachi isolati < soglia px
    ApplyMajorityDenoise,           // rimappa pixel anomali via majority vote 3×3
    MorphologyErode,                // morfologia: erode bordo opaco di 1px per iterazione
    MorphologyDilate,               // morfologia: dilata bordo opaco di 1px (edge padding)
    MorphologyOpen,                 // morfologia: erode poi dilata (rimuove protrusioni/pixel isolati)
    MorphologyClose,                // morfologia: dilata poi erode (chiude buchi sottili)
    RemoveIsolatedIslands,          // anomaly: rimuove componenti connesse < minSize px
    RemoveColorOutliers,            // anomaly: rimuove pixel con colore troppo distante dai vicini
    SelectArea,
    SelectAll,
    ClearSelection,
    ExportSelection,
    CropSelection,
    RemoveSelection,
    ToggleManualCrop,
    DetectSprites,
    GridSlice,
    SaveSelectedFrame,
    ExportAllFramesZip,
    OpenFloatingPaste,
    PasteDestination,
    PasteSource,
    CopyPasteSelection,
    ExitFloatingPaste,
    ApplyQuantize,
    AnalyzePalette,             // mostra colori unici dell'immagine senza quantizzare
    PaletteColorPickedAsBackground, // swatch cliccato → aggiorna il colore sfondo
    MirrorHorizontal,
    MirrorVertical,
    ResizeNearest,
    ExportPng,
    ExportJson,
    ActivatePipetteForBackground,
}
