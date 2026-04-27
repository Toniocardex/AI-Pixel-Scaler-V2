using System;
using System.Collections.Generic;
using System.Linq;
using AiPixelScaler.Core.Pipeline.Normalization;
using AiPixelScaler.Core.Pipeline.Slicing;
using AiPixelScaler.Desktop.Imaging;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Desktop.Views;

/// <summary>
/// Anteprima animazione modale: cicla i frame del documento corrente alla velocità
/// scelta. Tre modalità di loop (infinito / ping-pong / una sola volta), scrubber
/// manuale, FPS configurabile 1–60. I frame vengono pre-estratti come Bitmap Avalonia
/// all'apertura — niente decode/encode runtime, swap istantaneo.
/// </summary>
public partial class AnimationPreviewWindow : Window
{
    public enum LoopMode { Loop = 0, PingPong = 1, Once = 2 }

    private readonly List<Bitmap> _frameBitmaps = [];
    private int _currentIdx;
    private DispatcherTimer? _timer;
    private int _fps = 12;
    private bool _playing;
    private int _pingPongDir = 1;
    private bool _suppressFrameSliderEvent;

    private static readonly IBrush CheckerBgBrushA = new SolidColorBrush(Avalonia.Media.Color.FromArgb(0xff, 0x2a, 0x2a, 0x2e));
    private static readonly IBrush CheckerBgBrushB = new SolidColorBrush(Avalonia.Media.Color.FromArgb(0xff, 0x1e, 0x1e, 0x22));

    public AnimationPreviewWindow()
    {
        InitializeComponent();

        BtnPlay.Click             += (_, _) => TogglePlay();
        SliderFps.ValueChanged    += (_, e) => SetFps((int)e.NewValue);
        SliderFrame.ValueChanged  += (_, e) =>
        {
            if (_suppressFrameSliderEvent) return;
            SetFrame((int)e.NewValue);
        };
        CmbBackground.SelectionChanged += (_, _) => UpdateBackground();
        Closing                   += (_, _) => Cleanup();

        UpdateBackground();
    }

    /// <summary>
    /// Carica una nuova animazione. Se già in riproduzione, la ferma e azzera.
    /// </summary>
    public void LoadAnimation(Image<Rgba32> atlas, IReadOnlyList<SpriteCell> cells)
    {
        Cleanup(stopOnly: true);
        DisposeFrames();

        if (cells.Count == 0)
        {
            TxtFrameIdx.Text = "Nessun frame";
            return;
        }

        foreach (var c in cells)
        {
            using var crop = AtlasCropper.Crop(atlas, c.BoundsInAtlas);
            var bmp = Rgba32BitmapBridge.ToBitmap(crop);
            if (bmp is not null) _frameBitmaps.Add(bmp);
        }

        if (_frameBitmaps.Count == 0)
        {
            TxtFrameIdx.Text = "Frame vuoti";
            return;
        }

        _suppressFrameSliderEvent = true;
        SliderFrame.Maximum = _frameBitmaps.Count - 1;
        SliderFrame.Value   = 0;
        _suppressFrameSliderEvent = false;

        _currentIdx = 0;
        _pingPongDir = 1;
        UpdateFrame();

        Title = $"🎬 Anteprima animazione — {_frameBitmaps.Count} frame";
    }

    // ─── Controls ────────────────────────────────────────────────────────────

    private void TogglePlay()
    {
        if (_frameBitmaps.Count <= 1) return;
        _playing = !_playing;
        BtnPlay.Content = _playing ? "⏸  Pausa" : "▶  Play";
        EnsureTimer();
        if (_playing) _timer!.Start(); else _timer!.Stop();
    }

    private void EnsureTimer()
    {
        if (_timer is not null) return;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / _fps) };
        _timer.Tick += (_, _) => Advance();
    }

    private void SetFps(int fps)
    {
        _fps = Math.Clamp(fps, 1, 60);
        TxtFps.Text = $"{_fps} FPS";
        if (_timer is not null)
            _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / _fps);
    }

    private void SetFrame(int idx)
    {
        idx = Math.Clamp(idx, 0, Math.Max(0, _frameBitmaps.Count - 1));
        if (idx == _currentIdx) return;
        _currentIdx = idx;
        UpdateFrame();
    }

    private void Advance()
    {
        if (_frameBitmaps.Count <= 1) return;
        var mode = (LoopMode)CmbLoop.SelectedIndex;
        switch (mode)
        {
            case LoopMode.Loop:
                _currentIdx = (_currentIdx + 1) % _frameBitmaps.Count;
                break;

            case LoopMode.PingPong:
                var next = _currentIdx + _pingPongDir;
                if (next >= _frameBitmaps.Count)
                {
                    _pingPongDir = -1;
                    next = _frameBitmaps.Count - 2;
                }
                else if (next < 0)
                {
                    _pingPongDir = 1;
                    next = Math.Min(1, _frameBitmaps.Count - 1);
                }
                _currentIdx = next;
                break;

            case LoopMode.Once:
                if (_currentIdx < _frameBitmaps.Count - 1) _currentIdx++;
                else { TogglePlay(); return; }
                break;
        }
        UpdateFrame();
    }

    private void UpdateFrame()
    {
        if (_currentIdx < 0 || _currentIdx >= _frameBitmaps.Count) return;
        FrameImage.Source = _frameBitmaps[_currentIdx];
        TxtFrameIdx.Text = $"Frame {_currentIdx + 1} / {_frameBitmaps.Count}";

        _suppressFrameSliderEvent = true;
        SliderFrame.Value = _currentIdx;
        _suppressFrameSliderEvent = false;
    }

    private void UpdateBackground()
    {
        DisplayBorder.Background = CmbBackground.SelectedIndex switch
        {
            1 => Brushes.Black,
            2 => Brushes.White,
            3 => new SolidColorBrush(Avalonia.Media.Color.FromRgb(255, 0, 255)),
            _ => MakeCheckerBrush(),
        };
    }

    private static IBrush MakeCheckerBrush()
    {
        // Pattern 16×16 con checker 8×8: usa VisualBrush con DrawingGroup
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.DrawRectangle(CheckerBgBrushA, null, new Rect(0, 0, 8, 8));
            dc.DrawRectangle(CheckerBgBrushA, null, new Rect(8, 8, 8, 8));
            dc.DrawRectangle(CheckerBgBrushB, null, new Rect(8, 0, 8, 8));
            dc.DrawRectangle(CheckerBgBrushB, null, new Rect(0, 8, 8, 8));
        }
        return new DrawingBrush(group)
        {
            TileMode    = TileMode.Tile,
            DestinationRect = new RelativeRect(0, 0, 16, 16, RelativeUnit.Absolute),
            SourceRect      = new RelativeRect(0, 0, 16, 16, RelativeUnit.Absolute),
        };
    }

    private void Cleanup(bool stopOnly = false)
    {
        _timer?.Stop();
        _playing = false;
        BtnPlay.Content = "▶  Play";
        if (stopOnly) return;
        DisposeFrames();
        FrameImage.Source = null;
    }

    private void DisposeFrames()
    {
        foreach (var b in _frameBitmaps) b.Dispose();
        _frameBitmaps.Clear();
    }
}
