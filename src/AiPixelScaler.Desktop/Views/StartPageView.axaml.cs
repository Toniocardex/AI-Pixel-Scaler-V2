using System;
using AiPixelScaler.Desktop.ViewModels.Studios;
using Avalonia.Controls;

namespace AiPixelScaler.Desktop.Views;

public partial class StartPageView : UserControl
{
    public event EventHandler<StudioKind>? StudioSelected;

    public StartPageView()
    {
        InitializeComponent();
        BtnStartSprite.Click += (_, _) => StudioSelected?.Invoke(this, StudioKind.Sprite);
        BtnStartAnimation.Click += (_, _) => StudioSelected?.Invoke(this, StudioKind.Animation);
        BtnStartTileset.Click += (_, _) => StudioSelected?.Invoke(this, StudioKind.Tileset);
    }
}
