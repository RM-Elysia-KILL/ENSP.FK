using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace ENSP.ZD.Services;

/// <summary>
/// Screen capture and template-matching image recognition.
/// Uses grayscale normalized cross-correlation with integral-image O(1) statistics.
/// </summary>
public class ImageRecognitionService
{
    private readonly ConcurrentDictionary<string, Bitmap> _templateCache = new(StringComparer.OrdinalIgnoreCase);

    public Bitmap CaptureScreenRegion(int x, int y, int width, int height)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height));
        return bmp;
    }

    /// <summary>
    /// Capture a window's client area even when it's behind other windows.
    /// Uses PrintWindow — works on Windows 10+.
    /// </summary>
    public Bitmap? CaptureWindow(IntPtr hwnd)
    {
        IntPtr hBitmap = Win32Interop.CaptureWindowBitmap(hwnd, out int w, out int h);
        if (hBitmap == IntPtr.Zero || w <= 0 || h <= 0)
            return null;

        try
        {
            return Image.FromHbitmap(hBitmap);
        }
        finally
        {
            Win32Interop.DeleteObject(hBitmap);
        }
    }

    public Bitmap CaptureCursorRegion(int size = 48)
    {
        Win32Interop.GetCursorPos(out var pt);
        int half = size / 2;
        int x = Math.Max(0, pt.X - half);
        int y = Math.Max(0, pt.Y - half);
        return CaptureScreenRegion(x, y, size, size);
    }

    public static void SaveTemplate(Bitmap bmp, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bmp.Save(path, ImageFormat.Png);
    }

    public Bitmap? LoadTemplate(string path)
    {
        if (!File.Exists(path))
            return null;

        return _templateCache.GetOrAdd(path, p => new Bitmap(p));
    }

    /// <summary>
    /// Find a template within a screenshot. Runs synchronously; caller should wrap in Task.Run.
    /// Uses integral images for O(1) window statistics plus a step size for speed.
    /// </summary>
    public (System.Drawing.Point Location, double Confidence)? FindTemplate(
        Bitmap screenshot, Bitmap template,
        double minConfidence = 0.7,
        System.Drawing.Rectangle? searchRegion = null,
        int step = 2)
    {
        ArgumentNullException.ThrowIfNull(screenshot);
        ArgumentNullException.ThrowIfNull(template);

        if (template.Width > screenshot.Width || template.Height > screenshot.Height)
            return null;

        int sW = screenshot.Width, sH = screenshot.Height;
        int tW = template.Width, tH = template.Height;

        // Determine search bounds
        int sx0 = 0, sy0 = 0, sx1 = sW - tW, sy1 = sH - tH;
        if (searchRegion.HasValue)
        {
            var r = searchRegion.Value;
            sx0 = Math.Max(0, Math.Min(r.X, sW - tW));
            sy0 = Math.Max(0, Math.Min(r.Y, sH - tH));
            sx1 = Math.Min(sW - tW, Math.Max(0, r.X + r.Width - tW));
            sy1 = Math.Min(sH - tH, Math.Max(0, r.Y + r.Height - tH));
        }

        if (sx1 < sx0 || sy1 < sy0)
            return null;

        // Convert both to grayscale float arrays
        var screenGray = ToGrayscale(screenshot);
        var templGray = ToGrayscale(template);

        // Build integral images for O(1) window sums
        int iW = sW + 1, iH = sH + 1;
        var integral = new double[iW * iH];
        var sqIntegral = new double[iW * iH];

        for (int y = 1; y <= sH; y++)
        for (int x = 1; x <= sW; x++)
        {
            float v = screenGray[(y - 1) * sW + (x - 1)];
            double a = integral[(y - 1) * iW + x];
            double b = integral[y * iW + (x - 1)];
            double c = integral[(y - 1) * iW + (x - 1)];
            integral[y * iW + x] = a + b - c + v;
            sqIntegral[y * iW + x] = sqIntegral[(y - 1) * iW + x]
                                   + sqIntegral[y * iW + (x - 1)]
                                   - sqIntegral[(y - 1) * iW + (x - 1)]
                                   + (double)v * v;
        }

        // Template mean and std (precompute once)
        float tMean = 0, tStdSq = 0;
        for (int i = 0; i < tW * tH; i++)
            tMean += templGray[i];
        tMean /= tW * tH;

        for (int i = 0; i < tW * tH; i++)
        {
            float diff = templGray[i] - tMean;
            tStdSq += diff * diff;
        }
        int n = tW * tH;
        float tStd = MathF.Sqrt(tStdSq / n);
        if (tStd < 1e-6f)
            return null;

        double invN = 1.0 / n;
        double bestNcc = -2.0;
        int bestX = 0, bestY = 0;

        // Helper: O(1) window sum from integral image
        double WindowSum(double[] integ, int x, int y)
        {
            return integ[(y + tH) * iW + (x + tW)]
                 - integ[(y + tH) * iW + x]
                 - integ[y * iW + (x + tW)]
                 + integ[y * iW + x];
        }

        for (int sy = sy0; sy <= sy1; sy += step)
        for (int sx = sx0; sx <= sx1; sx += step)
        {
            // O(1) window mean and std
            double wSum = WindowSum(integral, sx, sy);
            double wMean = wSum * invN;
            double wSqSum = WindowSum(sqIntegral, sx, sy);
            double wVariance = (wSqSum * invN) - (wMean * wMean);
            if (wVariance < 1e-12)
                continue;
            float wStd = MathF.Sqrt((float)wVariance);

            // Cross-correlation numerator: Σ((I - μI)(T - μT))
            // = Σ(I*T) - n * μI * μT
            double crossCorr = 0;
            for (int ty = 0; ty < tH; ty++)
            for (int tx = 0; tx < tW; tx++)
            {
                float sv = screenGray[(sy + ty) * sW + (sx + tx)];
                crossCorr += sv * templGray[ty * tW + tx];
            }
            double num = crossCorr - n * wMean * tMean;
            double ncc = num / (n * (double)wStd * tStd);

            if (ncc > bestNcc)
            {
                bestNcc = ncc;
                bestX = sx;
                bestY = sy;
            }
        }

        // If stepped search produced a candidate, refine with 1px search in a small neighborhood
        if (step > 1 && bestNcc >= minConfidence)
        {
            int refineR = step;
            int rx0 = Math.Max(sx0, bestX - refineR);
            int ry0 = Math.Max(sy0, bestY - refineR);
            int rx1 = Math.Min(sx1, bestX + refineR);
            int ry1 = Math.Min(sy1, bestY + refineR);

            for (int sy = ry0; sy <= ry1; sy++)
            for (int sx = rx0; sx <= rx1; sx++)
            {
                double wSum = WindowSum(integral, sx, sy);
                double wMean = wSum * invN;
                double wSqSum = WindowSum(sqIntegral, sx, sy);
                double wVariance = (wSqSum * invN) - (wMean * wMean);
                if (wVariance < 1e-12) continue;
                float wStd = MathF.Sqrt((float)wVariance);

                double crossCorr = 0;
                for (int ty = 0; ty < tH; ty++)
                for (int tx = 0; tx < tW; tx++)
                    crossCorr += screenGray[(sy + ty) * sW + (sx + tx)] * templGray[ty * tW + tx];

                double num = crossCorr - n * wMean * tMean;
                double ncc = num / (n * (double)wStd * tStd);
                if (ncc > bestNcc)
                {
                    bestNcc = ncc;
                    bestX = sx;
                    bestY = sy;
                }
            }
        }

        if (bestNcc < minConfidence)
            return null;

        return (new System.Drawing.Point(bestX, bestY), bestNcc);
    }

    private static float[] ToGrayscale(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var result = new float[w * h];
        var rect = new Rectangle(0, 0, w, h);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        int stride = data.Stride;
        int byteCount = stride * h;
        var bytes = new byte[byteCount];
        Marshal.Copy(data.Scan0, bytes, 0, byteCount);
        bmp.UnlockBits(data);

        for (int y = 0; y < h; y++)
        {
            int rowOffset = y * stride;
            for (int x = 0; x < w; x++)
            {
                int offset = rowOffset + x * 3;
                float r = bytes[offset + 2]; // BGR in GDI
                float g = bytes[offset + 1];
                float b = bytes[offset];
                result[y * w + x] = 0.299f * r + 0.587f * g + 0.114f * b;
            }
        }

        return result;
    }

    public void ClearCache()
    {
        foreach (var (_, bmp) in _templateCache)
            bmp.Dispose();
        _templateCache.Clear();
    }
}
