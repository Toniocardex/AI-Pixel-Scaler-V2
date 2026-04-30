using System;
using System.Threading.Tasks;
using AiPixelScaler.Core.Pipeline.Editor;
using AiPixelScaler.Core.Pipeline.Imaging;
using AiPixelScaler.Desktop.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Desktop.Services;

internal sealed class FloatingPasteCoordinator
{
    private readonly EditorSurface _editor;
    private Image<Rgba32>? _source;
    private Bitmap? _previewBitmap;
    private double _displayScale = 1.0;
    private int _destX;
    private int _destY;

    public FloatingPasteCoordinator(EditorSurface editor) => _editor = editor;

    public bool HasActiveSession => _source is not null;

    public void ClearSession()
    {
        _source?.Dispose();
        _source = null;
        _previewBitmap?.Dispose();
        _previewBitmap = null;
        _displayScale = 1.0;
        _editor.ClearFloatingOverlay();
    }

    public async Task TryBeginAsync(
        Image<Rgba32>? document,
        IClipboard? clipboard,
        Func<bool> isPasteBlocked,
        Func<int, int, int, int, Task<PasteOversizeChoice>> showOversizeDialogAsync,
        Action<string> setStatus)
    {
        if (document is null)
        {
            setStatus("Apri prima un'immagine.");
            return;
        }

        if (isPasteBlocked())
        {
            setStatus("Esci dalla modalità workbench, atlas pulito o anteprima tile prima di incollare dagli appunti.");
            return;
        }

        if (clipboard is null)
        {
            setStatus("Appunti non disponibili.");
            return;
        }

        Image<Rgba32>? loaded;
        try
        {
            loaded = await ClipboardBitmapInterop.TryReadImageAsync(clipboard).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            setStatus($"Lettura appunti: {ex.Message}");
            return;
        }

        if (loaded is null)
        {
            setStatus("Appunti: nessuna immagine (PNG/bitmap).");
            return;
        }

        ClearSession();
        _source = loaded;

        var dw = document.Width;
        var dh = document.Height;
        double scale = 1.0;

        if (loaded.Width > dw || loaded.Height > dh)
        {
            var choice = await showOversizeDialogAsync(loaded.Width, loaded.Height, dw, dh).ConfigureAwait(true);
            if (choice == PasteOversizeChoice.Cancel)
            {
                ClearSession();
                setStatus("Incolla annullato.");
                return;
            }

            if (choice == PasteOversizeChoice.FitToCanvas)
                scale = FloatingPasteGeometry.ComputeUniformFitScale(loaded.Width, loaded.Height, dw, dh);
        }

        _displayScale = scale;
        var (dispW, dispH) = FloatingPasteGeometry.ComputeDisplayDimensions(loaded.Width, loaded.Height, scale);
        var (cx, cy) = FloatingPasteGeometry.ComputeCenteredTopLeft(dw, dh, dispW, dispH);
        _destX = cx;
        _destY = cy;

        Image<Rgba32>? resizedPreview = null;
        try
        {
            if (Math.Abs(scale - 1.0) > 1e-9)
                resizedPreview = NearestNeighborResize.Resize(loaded, dispW, dispH);

            var forBridge = resizedPreview ?? loaded;
            _previewBitmap = Imaging.Rgba32BitmapBridge.ToBitmap(forBridge);
        }
        finally
        {
            resizedPreview?.Dispose();
        }

        if (_previewBitmap is null)
        {
            ClearSession();
            setStatus("Impossibile creare anteprima incolla.");
            return;
        }

        _editor.SetFloatingOverlay(_previewBitmap, _destX, _destY, dispW, dispH);
        setStatus("Incolla fluttuante: trascina · Invio conferma · Esc annulla.");
    }

    public void OnOverlayMoved(FloatingOverlayMoveEventArgs e)
    {
        _destX = e.X;
        _destY = e.Y;
        _editor.UpdateFloatingOverlayPosition(e.X, e.Y);
    }

    public bool TryCommit(Image<Rgba32>? document, Func<bool> pushUndo, Action<string> setStatus, Action refreshView)
    {
        if (document is null || _source is null)
            return false;

        try
        {
            if (!pushUndo())
                return false;

            FloatingPasteComposer.Commit(document, _source, _destX, _destY, _displayScale);
            ClearSession();
            refreshView();
            setStatus("Incolla confermato sul canvas.");
            return true;
        }
        catch (Exception ex)
        {
            setStatus($"Errore commit incolla: {ex.Message}");
            return false;
        }
    }

    public void Cancel(Action<string> setStatus)
    {
        ClearSession();
        setStatus("Incolla fluttuante annullato.");
    }
}
