using SkiaSharp;

namespace MauiJohnWick1;

public enum SamplingMode
{
    SinglePixel,
    AverageArea
}

public sealed record ColorSample(
    int Red,
    int Green,
    int Blue,
    double Lightness,
    double A,
    double B,
    int PixelCount);

public static class ColorSampler
{
    public static SKBitmap Decode(byte[] imageBytes)
    {
        using var data = SKData.CreateCopy(imageBytes);
        using var codec = SKCodec.Create(data);
        SKBitmap bitmap = SKBitmap.Decode(data)
            ?? throw new InvalidOperationException("The captured image could not be decoded.");

        SKBitmap orientedBitmap = codec?.EncodedOrigin switch
        {
            SKEncodedOrigin.RightTop => Rotate(bitmap, 90),
            SKEncodedOrigin.LeftBottom => Rotate(bitmap, 270),
            _ => bitmap
        };

        return orientedBitmap.Width > orientedBitmap.Height
            ? Rotate(orientedBitmap, 90)
            : orientedBitmap;
    }

    private static SKBitmap Rotate(SKBitmap bitmap, float degrees)
    {
        var rotated = new SKBitmap(bitmap.Height, bitmap.Width);
        using var canvas = new SKCanvas(rotated);

        if (degrees == 90)
            canvas.Translate(rotated.Width, 0);
        else
            canvas.Translate(0, rotated.Height);

        canvas.RotateDegrees(degrees);
        canvas.DrawBitmap(bitmap, 0, 0);
        bitmap.Dispose();
        return rotated;
    }

    public static (int Width, int Height) GetImageSize(byte[] imageBytes)
    {
        using SKBitmap bitmap = Decode(imageBytes);
        return GetImageSize(bitmap);
    }

    public static (int Width, int Height) GetImageSize(SKBitmap bitmap) =>
        (bitmap.Width, bitmap.Height);

    public static ColorSample Sample(byte[] imageBytes, int x, int y, int width, int height, SamplingMode mode)
    {
        using SKBitmap bitmap = Decode(imageBytes);
        return Sample(bitmap, x, y, width, height, mode);
    }

    public static ColorSample Sample(SKBitmap bitmap, int x, int y, int width, int height, SamplingMode mode)
    {
        if (bitmap.Width == 0 || bitmap.Height == 0)
            throw new InvalidOperationException("The captured image has no pixels.");

        x = Math.Clamp(x, 0, bitmap.Width - 1);
        y = Math.Clamp(y, 0, bitmap.Height - 1);

        if (mode == SamplingMode.SinglePixel)
        {
            SKColor color = bitmap.GetPixel(x, y);
            (double singleLightness, double singleA, double singleB) = ToLab(color.Red, color.Green, color.Blue);
            return new ColorSample(color.Red, color.Green, color.Blue, singleLightness, singleA, singleB, 1);
        }

        int right = Math.Clamp(x + Math.Max(width, 1), x + 1, bitmap.Width);
        int bottom = Math.Clamp(y + Math.Max(height, 1), y + 1, bitmap.Height);
        long redTotal = 0;
        long greenTotal = 0;
        long blueTotal = 0;
        double linearRedTotal = 0;
        double linearGreenTotal = 0;
        double linearBlueTotal = 0;
        int pixelCount = 0;

        for (int pixelY = y; pixelY < bottom; pixelY++)
        {
            for (int pixelX = x; pixelX < right; pixelX++)
            {
                SKColor color = bitmap.GetPixel(pixelX, pixelY);
                redTotal += color.Red;
                greenTotal += color.Green;
                blueTotal += color.Blue;

                linearRedTotal += SrgbToLinear(color.Red / 255.0);
                linearGreenTotal += SrgbToLinear(color.Green / 255.0);
                linearBlueTotal += SrgbToLinear(color.Blue / 255.0);
                pixelCount++;
            }
        }

        (double lightness, double a, double b) = ToLabFromLinearRgb(
            linearRedTotal / pixelCount,
            linearGreenTotal / pixelCount,
            linearBlueTotal / pixelCount,
            0.95047, 1.0, 1.08883);

        return new ColorSample(
            (int)Math.Round((double)redTotal / pixelCount),
            (int)Math.Round((double)greenTotal / pixelCount),
            (int)Math.Round((double)blueTotal / pixelCount),
            lightness,
            a,
            b,
            pixelCount);
    }

    public static ColorSample ApplyWhiteStandard(ColorSample sample, ColorSample whiteStandard)
    {
        (double red, double green, double blue) = ToLinearRgb(sample.Red, sample.Green, sample.Blue);
        (double whiteRed, double whiteGreen, double whiteBlue) = ToLinearRgb(whiteStandard.Red, whiteStandard.Green, whiteStandard.Blue);
        (double whiteX, double whiteY, double whiteZ) = ToXyz(whiteRed, whiteGreen, whiteBlue);
        (double lightness, double a, double b) = ToLabFromLinearRgb(red, green, blue, whiteX / whiteY, 1.0, whiteZ / whiteY);
        return sample with { Lightness = lightness, A = a, B = b };
    }

    public static double DeltaE2000(ColorSample first, ColorSample second)
    {
        double c1 = Math.Sqrt(first.A * first.A + first.B * first.B);
        double c2 = Math.Sqrt(second.A * second.A + second.B * second.B);
        double averageC = (c1 + c2) / 2;
        double g = 0.5 * (1 - Math.Sqrt(Math.Pow(averageC, 7) / (Math.Pow(averageC, 7) + Math.Pow(25, 7))));
        double a1 = (1 + g) * first.A;
        double a2 = (1 + g) * second.A;
        double c1Prime = Math.Sqrt(a1 * a1 + first.B * first.B);
        double c2Prime = Math.Sqrt(a2 * a2 + second.B * second.B);
        double h1 = Hue(a1, first.B);
        double h2 = Hue(a2, second.B);
        double deltaL = second.Lightness - first.Lightness;
        double deltaC = c2Prime - c1Prime;
        double deltaH = 2 * Math.Sqrt(c1Prime * c2Prime) * Math.Sin((h2 - h1) * Math.PI / 360);
        double averageL = (first.Lightness + second.Lightness) / 2;
        double averageCPrime = (c1Prime + c2Prime) / 2;
        double averageH = Math.Abs(h1 - h2) <= 180 ? (h1 + h2) / 2 : (h1 + h2 + 360) / 2;
        double t = 1 - 0.17 * Math.Cos((averageH - 30) * Math.PI / 180) + 0.24 * Math.Cos(2 * averageH * Math.PI / 180) + 0.32 * Math.Cos((3 * averageH + 6) * Math.PI / 180) - 0.20 * Math.Cos((4 * averageH - 63) * Math.PI / 180);
        double sl = 1 + 0.015 * Math.Pow(averageL - 50, 2) / Math.Sqrt(20 + Math.Pow(averageL - 50, 2));
        double sc = 1 + 0.045 * averageCPrime;
        double sh = 1 + 0.015 * averageCPrime * t;
        double rt = -2 * Math.Sqrt(Math.Pow(averageCPrime, 7) / (Math.Pow(averageCPrime, 7) + Math.Pow(25, 7))) * Math.Sin(60 * Math.Exp(-Math.Pow((averageH - 275) / 25, 2)) * Math.PI / 180);
        return Math.Sqrt(Math.Pow(deltaL / sl, 2) + Math.Pow(deltaC / sc, 2) + Math.Pow(deltaH / sh, 2) + rt * (deltaC / sc) * (deltaH / sh));
    }

    private static double Hue(double a, double b)
    {
        double hue = Math.Atan2(b, a) * 180 / Math.PI;
        return hue >= 0 ? hue : hue + 360;
    }

    private static (double Red, double Green, double Blue) ToLinearRgb(int red, int green, int blue) =>
        (SrgbToLinear(red / 255.0), SrgbToLinear(green / 255.0), SrgbToLinear(blue / 255.0));

    private static (double X, double Y, double Z) ToXyz(double red, double green, double blue) =>
        (red * 0.4124564 + green * 0.3575761 + blue * 0.1804375,
         red * 0.2126729 + green * 0.7151522 + blue * 0.0721750,
         red * 0.0193339 + green * 0.1191920 + blue * 0.9503041);

    private static (double Lightness, double A, double B) ToLabFromLinearRgb(double red, double green, double blue, double whiteX, double whiteY, double whiteZ)
    {
        (double x, double y, double z) = ToXyz(red, green, blue);
        return ToLabFromXyz(x, y, z, whiteX, whiteY, whiteZ);
    }

    private static (double Lightness, double A, double B) ToLabFromXyz(double x, double y, double z, double whiteX, double whiteY, double whiteZ)
    {
        double fx = LabPivot(x / whiteX);
        double fy = LabPivot(y / whiteY);
        double fz = LabPivot(z / whiteZ);
        return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    private static (double Lightness, double A, double B) ToLab(byte red, byte green, byte blue)
    {
        double r = SrgbToLinear(red / 255.0);
        double g = SrgbToLinear(green / 255.0);
        double b = SrgbToLinear(blue / 255.0);

        double x = (r * 0.4124564 + g * 0.3575761 + b * 0.1804375) / 0.95047;
        double y = (r * 0.2126729 + g * 0.7151522 + b * 0.0721750) / 1.00000;
        double z = (r * 0.0193339 + g * 0.1191920 + b * 0.9503041) / 1.08883;

        double fx = LabPivot(x);
        double fy = LabPivot(y);
        double fz = LabPivot(z);

        return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    private static double SrgbToLinear(double value) =>
        value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);

    private static double LabPivot(double value) =>
        value > 0.008856
            ? Math.Pow(value, 1.0 / 3.0)
            : (7.787 * value) + (16.0 / 116.0);
}