namespace AiPixelScaler.Desktop.Views.Studios;

public enum AnimationStudioAction
{
    OpenAnimationPreview,
    OpenSandbox,
    EnterFrameWorkbench,
    CommitFrameWorkbench,
    CancelFrameWorkbench,
    AlignAllCenter,
    AlignAllBaseline,
    ResetAll,
    AlignSelectedCenter,
    AlignSelectedBaseline,
    AlignSelectedLayoutCenter,
    ResetSelected,
    RunGlobalScan,
    RunBaselineAlignment,
    RunCenterInCells,
    ImportFromVideo,        // estrai frame PNG da video MP4 H.264 via FFmpeg
    ExportFramesZip,
}
