using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Plugins;

public interface IImageProcessorPlugin
{
    string Id { get; }
    string DisplayName { get; }
    string Version { get; }

    Image<Rgba32> Process(Image<Rgba32> image);
}
