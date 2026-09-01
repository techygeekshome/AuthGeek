using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace AuthGeek.Core.Services;

/// <summary>
/// Reads a QR code out of an image, and draws one.
///
/// Reading matters because nobody wants to type a thirty-two character secret by hand, and the
/// setup QR on a website is right there on screen. Drawing matters for the opposite reason: an
/// account added here has to be able to get onto a phone as well, and the way phones take one is
/// by camera.
///
/// This works on raw pixels rather than on a file, so nothing about loading images has to live in
/// the Core project. Whoever calls it decodes the picture, which is a job the user interface
/// already has a library for.
/// </summary>
public static class QrCode
{
    /// <summary>
    /// Finds a QR code in a block of RGB pixels, or null if there is not one.
    ///
    /// TryHarder is on. It is slower, and on a screenshot of a web page at an odd scale it is
    /// often the difference between finding the code and not, which is the whole job.
    /// </summary>
    public static string? Read(byte[] rgb, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;
        if (rgb.Length < width * height * 3)
            throw new ArgumentException("There are fewer pixels there than the size claims.", nameof(rgb));

        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                TryInverted = true,
                PossibleFormats = new[] { BarcodeFormat.QR_CODE }
            }
        };

        return reader.Decode(new RGBLuminanceSource(rgb, width, height))?.Text;
    }

    /// <summary>
    /// A QR code as a grid of true and false, ready to be drawn as squares.
    ///
    /// Returned as booleans rather than as an image so the Core project stays free of any drawing
    /// library, and so the caller can draw it at whatever size and colour suits the screen.
    /// </summary>
    public static bool[,] Draw(string text, int size = 33)
    {
        if (string.IsNullOrEmpty(text)) throw new ArgumentException("There is nothing to encode.", nameof(text));

        var writer = new QRCodeWriter();
        var matrix = writer.encode(text, BarcodeFormat.QR_CODE, size, size, new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.MARGIN] = 1,
            [EncodeHintType.ERROR_CORRECTION] = ZXing.QrCode.Internal.ErrorCorrectionLevel.M,
            [EncodeHintType.CHARACTER_SET] = "UTF-8"
        });

        var grid = new bool[matrix.Width, matrix.Height];
        for (var x = 0; x < matrix.Width; x++)
        for (var y = 0; y < matrix.Height; y++)
            grid[x, y] = matrix[x, y];

        return grid;
    }

    /// <summary>Draws a QR and hands it back as RGB pixels. Used by the round trip check.</summary>
    public static (byte[] Rgb, int Width, int Height) DrawAsPixels(string text, int scale = 8)
    {
        var grid = Draw(text);
        var cells = grid.GetLength(0);
        var size = cells * scale;
        var rgb = new byte[size * size * 3];

        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var dark = grid[x / scale, y / scale];
            var at = (y * size + x) * 3;
            var v = dark ? (byte)0 : (byte)255;
            rgb[at] = v; rgb[at + 1] = v; rgb[at + 2] = v;
        }

        return (rgb, size, size);
    }
}
