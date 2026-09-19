using Lifestyle.Modules.Media.Internal;
using Shouldly;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace Lifestyle.UnitTests.Media;

/// <summary>
/// The square white canvas (docs/08 §6.3). What makes a marketplace grid look professional is not
/// background removal but consistency — one shape, one ground — and this is the cheap way to get it.
/// </summary>
public sealed class ImageSharpProcessorTests
{
    private static readonly ImageSharpProcessor Processor = new();

    private static MemoryStream Photo(int width, int height, Color? fill = null)
    {
        using var image = new Image<Rgba32>(width, height);
        image.Mutate(x => x.BackgroundColor(fill ?? Color.Red));

        var stream = new MemoryStream();
        image.SaveAsJpeg(stream, new JpegEncoder { Quality = 90 });
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task A_wide_photo_is_squared_onto_a_white_canvas()
    {
        await using var source = Photo(1600, 500);

        using var resized = await Processor.ResizeAsync(source, 600, squareCanvas: true, CancellationToken.None);

        resized.ShouldNotBeNull();
        resized.Width.ShouldBe(600);
        resized.Height.ShouldBe(600);
    }

    [Fact]
    public async Task A_tall_photo_is_squared_too_so_the_grid_is_one_shape()
    {
        await using var source = Photo(500, 1600);

        using var resized = await Processor.ResizeAsync(source, 600, squareCanvas: true, CancellationToken.None);

        resized.ShouldNotBeNull();
        resized.Width.ShouldBe(600);
        resized.Height.ShouldBe(600);
    }

    /// <summary>The padding must be white, not the transparent-to-black a naive pad would give.</summary>
    [Fact]
    public async Task The_padding_is_white_and_the_photo_is_not_cropped()
    {
        await using var source = Photo(1600, 500);

        using var resized = await Processor.ResizeAsync(source, 600, squareCanvas: true, CancellationToken.None);
        resized.ShouldNotBeNull();

        using var output = await Image.LoadAsync<Rgba32>(resized.Content, CancellationToken.None);

        // A wide photo leaves bands top and bottom.
        var topBand = output[300, 4];
        topBand.R.ShouldBeGreaterThan((byte)240);
        topBand.G.ShouldBeGreaterThan((byte)240);
        topBand.B.ShouldBeGreaterThan((byte)240);

        // The middle is still the photograph — padding, never cropping.
        var centre = output[300, 300];
        centre.R.ShouldBeGreaterThan((byte)150);
        centre.G.ShouldBeLessThan((byte)110);
    }

    [Fact]
    public async Task Without_the_canvas_the_original_aspect_is_kept()
    {
        await using var source = Photo(1600, 500);

        using var resized = await Processor.ResizeAsync(source, 600, squareCanvas: false, CancellationToken.None);

        resized.ShouldNotBeNull();
        resized.Width.ShouldBe(600);
        resized.Height.ShouldNotBe(600);
    }

    /// <summary>
    /// A banner is uploaded without the canvas precisely so it keeps its shape; squaring one would
    /// letterbox the shop header.
    /// </summary>
    [Fact]
    public async Task An_image_already_smaller_than_the_variant_is_left_alone()
    {
        await using var source = Photo(200, 200);

        var resized = await Processor.ResizeAsync(source, 600, squareCanvas: true, CancellationToken.None);

        resized.ShouldBeNull();
    }

    [Fact]
    public async Task Capture_time_is_null_when_the_file_has_no_exif()
    {
        await using var source = Photo(800, 600);

        var metadata = await Processor.ReadMetadataAsync(source, CancellationToken.None);

        metadata.ShouldNotBeNull();
        metadata.Width.ShouldBe(800);
        metadata.Height.ShouldBe(600);
        metadata.CapturedAt.ShouldBeNull();
    }
}
