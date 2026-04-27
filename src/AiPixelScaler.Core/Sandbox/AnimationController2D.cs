namespace AiPixelScaler.Core.Sandbox;

public sealed class AnimationController2D
{
    public AnimationController2D()
    {
        Clips = new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);
    }

    public Dictionary<string, AnimationClip> Clips { get; }
    public string? CurrentClip { get; private set; }
    public int FrameIndex { get; private set; }
    public double Elapsed { get; private set; }
    public double? FixedFps { get; set; } = 8;

    public void SetClip(string name)
    {
        if (CurrentClip == name) return;
        CurrentClip = name;
        FrameIndex = 0;
        Elapsed = 0;
    }

    public void Update(double deltaTime)
    {
        if (CurrentClip is null || !Clips.TryGetValue(CurrentClip, out var clip) || clip.FrameCount == 0)
            return;
        var fps = FixedFps ?? clip.Fps;
        if (fps <= 0) return;
        Elapsed += deltaTime;
        var spf = 1.0 / fps;
        var total = (int)(Elapsed / spf);
        FrameIndex = total % clip.FrameCount;
    }

    public int GetCurrentFrameId() => Clips.TryGetValue(CurrentClip ?? "", out var c) && c.FrameCount > 0
        ? c.Frames[FrameIndex % c.FrameCount]
        : 0;
}

public sealed class AnimationClip
{
    public required int[] Frames { get; init; }
    public double Fps { get; init; } = 8;
    public int FrameCount => Frames.Length;
}
