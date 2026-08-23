using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Lifestyle.Modules.Media.Internal;

/// <summary>
/// ImageSharp implementation of <see cref="IImageProcessor"/>.
/// <para>
/// Licensing note: SixLabors.ImageSharp 3.x ships under the Six Labors Split License — free for
/// open-source and small organisations, commercial licence required above their revenue threshold.
/// Flagged in docs/04 as an open decision; the interface exists so swapping to Magick.NET or a
/// sidecar service is a one-class change.
/// </para>
/// </summary>
internal sealed class ImageSharpProcessor : IImageProcessor
{
    public async Task<(int Width, int Height)?> ReadDimensionsAsync(Stream image, CancellationToken ct)
    {
        try
        {
            // Reads the header only — does not decode pixels, so a huge file costs nothing here.
            var info = await Image.IdentifyAsync(image, ct);
            return (info.Width, info.Height);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
        {
            return null;
        }
    }

    public async Task<ProcessedImage?> ResizeAsync(Stream source, int maxEdge, CancellationToken ct)
    {
        using var image = await Image.LoadAsync(source, ct);

        // Never enlarge: upscaling produces a bigger file that looks worse than the original.
        if (image.Width <= maxEdge && image.Height <= maxEdge) return null;

        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(maxEdge, maxEdge),
            Mode = ResizeMode.Max,
            Sampler = KnownResamplers.Lanczos3
        }));

        // Strip EXIF: orientation is already baked in by the resize, and camera metadata routinely
        // carries GPS coordinates a seller did not mean to publish.
        image.Metadata.ExifProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.XmpProfile = null;

        var output = new MemoryStream();
        await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = 82 }, ct);
        output.Position = 0;

        return new ProcessedImage(output, image.Width, image.Height, "image/jpeg");
    }
}
