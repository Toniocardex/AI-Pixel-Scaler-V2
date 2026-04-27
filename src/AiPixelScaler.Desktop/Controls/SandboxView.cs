using System;
using System.Collections.Generic;
using AiPixelScaler.Core.Geometry;
using AiPixelScaler.Core.Sandbox;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace AiPixelScaler.Desktop.Controls;

public class SandboxView : Control
{
    private readonly HashSet<Key> _keys = new();
    private readonly DispatcherTimer _timer;
    private KinematicBody2D _body;
    private readonly KinematicParams _params;
    private readonly World2D _world;
    private readonly AnimationController2D _anim;

    public SandboxView()
    {
        Focusable = true;
        ClipToBounds = true;
        _body = new KinematicBody2D { PosX = 200, PosY = 200, VelX = 0, VelY = 0 };
        _params = new KinematicParams { Acceleration = 1200, MaxSpeed = 280, Friction = 0.12 };
        _world = new World2D { GroundY = 420 };
        _world.Obstacles.Add(new AxisAlignedBox(350, 250, 420, 380));
        _anim = new AnimationController2D { FixedFps = 6 };
        _anim.Clips["Idle"] = new AnimationClip { Frames = new[] { 0, 1 }, Fps = 6 };
        _anim.SetClip("Idle");

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 60.0) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    private void Tick()
    {
        var ix = 0.0;
        var iy = 0.0;
        if (_keys.Contains(Key.W) || _keys.Contains(Key.Up)) iy -= 1;
        if (_keys.Contains(Key.S) || _keys.Contains(Key.Down)) iy += 1;
        if (_keys.Contains(Key.A) || _keys.Contains(Key.Left)) ix -= 1;
        if (_keys.Contains(Key.D) || _keys.Contains(Key.Right)) ix += 1;

        _anim.Update(1.0 / 60.0);
        KinematicStep.Integrate(ref _body, ix, iy, 1.0 / 60.0, _params);

        const int pw = 32;
        const int ph = 48;
        var halfW = pw / 2.0;
        var player = new AxisAlignedBox(
            (int)Math.Floor(_body.PosX - halfW),
            (int)Math.Floor(_body.PosY - ph),
            (int)Math.Floor(_body.PosX - halfW + pw),
            (int)Math.Floor(_body.PosY));

        if (player.MaxY > _world.GroundY)
        {
            var diff = player.MaxY - _world.GroundY;
            _body.PosY -= diff;
        }

        foreach (var o in _world.Obstacles)
        {
            if (!AxisAlignedBox.Intersects(player, o))
                continue;
            var overlapX = Math.Min(player.MaxX, o.MaxX) - Math.Max(player.MinX, o.MinX);
            var overlapY = Math.Min(player.MaxY, o.MaxY) - Math.Max(player.MinY, o.MinY);
            if (overlapX < overlapY)
            {
                if (_body.PosX < (o.MinX + o.MaxX) / 2.0)
                    _body.PosX -= overlapX;
                else
                    _body.PosX += overlapX;
            }
            else
            {
                if (_body.PosY < (o.MinY + o.MaxY) / 2.0)
                    _body.PosY -= overlapY;
                else
                    _body.PosY += overlapY;
            }
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var w = Bounds.Width;
        var h = Bounds.Height;
        context.FillRectangle(Brushes.Black, new Rect(0, 0, w, h));
        context.DrawLine(new Pen(Brushes.Gray, 2), new Avalonia.Point(0, _world.GroundY), new Avalonia.Point(w, _world.GroundY));
        foreach (var o in _world.Obstacles)
            context.FillRectangle(Brushes.DarkRed, new Rect(o.MinX, o.MinY, o.Width, o.Height));

        const int pw = 32;
        const int ph = 48;
        var frame = _anim.GetCurrentFrameId();
        var c = frame == 0
            ? Avalonia.Media.Color.FromArgb(0xff, 0x3c, 0xc7, 0x9e)
            : Avalonia.Media.Color.FromArgb(0xff, 0x6e, 0x9e, 0xff);
        var brush = new SolidColorBrush(c);
        var halfW = pw / 2.0;
        var rect = new Rect(_body.PosX - halfW, _body.PosY - ph, pw, ph);
        context.FillRectangle(brush, rect);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        _keys.Add(e.Key);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        _keys.Remove(e.Key);
    }
}
