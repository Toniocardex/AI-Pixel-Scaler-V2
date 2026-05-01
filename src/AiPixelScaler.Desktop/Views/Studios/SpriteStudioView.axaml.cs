using System;
using Avalonia.Controls;

namespace AiPixelScaler.Desktop.Views.Studios;

public partial class SpriteStudioView : UserControl
{
    public event EventHandler<SpriteStudioAction>? ActionRequested;

    public SpriteStudioView()
    {
        InitializeComponent();

        BtnSpriteOpen.Click += (_, _) => Request(SpriteStudioAction.OpenImage);
        BtnSpriteDefault.Click += (_, _) => Request(SpriteStudioAction.ApplyDefaultPreset);
        BtnSpriteCleanup.Click += (_, _) => Request(SpriteStudioAction.RunCleanup);
        BtnSpriteOneClick.Click += (_, _) => Request(SpriteStudioAction.RunOneClickCleanup);
        BtnSpriteSelect.Click += (_, _) => Request(SpriteStudioAction.SelectArea);
        BtnSpriteCrop.Click += (_, _) => Request(SpriteStudioAction.CropSelection);
        BtnSpriteRemove.Click += (_, _) => Request(SpriteStudioAction.RemoveSelection);
        BtnSpriteDetect.Click += (_, _) => Request(SpriteStudioAction.DetectSprites);
        BtnSpriteGridSlice.Click += (_, _) => Request(SpriteStudioAction.GridSlice);
        BtnSpriteFloatingPaste.Click += (_, _) => Request(SpriteStudioAction.OpenFloatingPaste);
        BtnSpriteQuantizeOpen.Click += (_, _) => Request(SpriteStudioAction.OpenQuantize);
        BtnSpriteQuantizeApply.Click += (_, _) => Request(SpriteStudioAction.ApplyQuantize);
        BtnSpriteMirrorH.Click += (_, _) => Request(SpriteStudioAction.MirrorHorizontal);
        BtnSpriteMirrorV.Click += (_, _) => Request(SpriteStudioAction.MirrorVertical);
        BtnSpriteExportPng.Click += (_, _) => Request(SpriteStudioAction.ExportPng);
        BtnSpriteExportJson.Click += (_, _) => Request(SpriteStudioAction.ExportJson);
    }

    private void Request(SpriteStudioAction action) => ActionRequested?.Invoke(this, action);
}
