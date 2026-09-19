using System.Globalization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
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
    public async Task<ImageMetadata?> ReadMetadataAsync(Stream image, CancellationToken ct)
    {
        try
        {
            // Reads the header only — does not decode pixels, so a huge file costs nothing here.
            var info = await Image.IdentifyAsync(image, ct);
            return new ImageMetadata(info.Width, info.Height, CaptureTimeOf(info.Metadata.ExifProfile));
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
        {
            return null;
        }
    }

    /// <summary>
    /// EXIF <c>DateTimeOriginal</c>, which cameras write as "yyyy:MM:dd HH:mm:ss" with no zone.
    /// <para>
    /// Treated as UTC rather than local: the value is only ever compared with other photos from the
    /// same shoot to find the gaps between them (docs/08 §4.3), so a consistent offset is all that
    /// matters and guessing the server's zone would be worse than not guessing.
    /// </para>
    /// </summary>
    private static DateTimeOffset? CaptureTimeOf(ExifProfile? exif)
    {
        if (exif is null) return null;

        if (!exif.TryGetValue(ExifTag.DateTimeOriginal, out var tag)
            && !exif.TryGetValue(ExifTag.DateTimeDigitized, out tag))
            return null;

        var text = tag?.Value;
        if (string.IsNullOrWhiteSpace(text)) return null;

        return DateTime.TryParseExact(
            text, "yyyy:MM:dd HH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? new DateTimeOffset(parsed, TimeSpan.Zero)
            : null;
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
