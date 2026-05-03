namespace AiPixelScaler.Desktop.Views.Studios;

public enum SpriteStudioAction
{
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
    ApplyDenoise,
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
