namespace ANX.Reporting.Tests;

using ANX.Control;
using ANX.Core;
using SkiaSharp;

/// <summary>
/// 存盘 PNG 上的曲线标注（用户 2026-10-15：实时曲线画了修正直线 / 修正原点 / 拟合区间两端，
/// 存盘图里没有）。这里按"像素真的不一样"来验，不看文件大小那类旁证。
/// </summary>
public sealed class CurveImageRendererTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"anx-png-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>一条上升—峰值—破坏的曲线（荷载 kN / 跨中挠度 mm）。</summary>
    private static List<CurvePoint> Curve() =>
        [new(0, 0), new(0.2, 0.6), new(0.4, 1.4), new(0.6, 2.0), new(0.8, 1.5), new(1.0, 0.9)];

    /// <summary>y = 4x − 0.1：修正原点 0.025 mm，拟合区间 10% / 80%。</summary>
    private static CurveAnnotation Fit() => new(4.0, -0.1, 0.025);

    [Fact]
    public void 有标注的图与没标注的图_像素上确实不同()
    {
        var plain = Decode(Render("plain.png", annotation: null));
        var marked = Decode(Render("marked.png", Fit()));

        Assert.Equal(plain.Width, marked.Width);
        Assert.Equal(plain.Height, marked.Height);
        Assert.True(
            CountDifferingPixels(plain, marked) > 0,
            "带标注的图必须与不带标注的不同 —— 否则说明标注没画进去"
        );
    }

    [Fact]
    public void 峰值标记有没有拟合都画_用界面同款的橙红()
    {
        var plain = CountOrangeRed(Decode(Render("plain.png", annotation: null)));
        var marked = CountOrangeRed(Decode(Render("marked.png", Fit())));

        // 峰值点与拟合无关：判定器关掉原点修正时图上只有它（口径与实时曲线一致）
        Assert.True(plain > 20, $"没拟合的图上也该有峰值标记，实际 {plain} 像素");
        Assert.True(marked > 20, $"带标注的图上该有峰值标记，实际 {marked} 像素");
    }

    [Fact]
    public void 空曲线也出图_不抛异常()
    {
        var path = Render("empty.png", annotation: null, curve: []);

        Assert.True(File.Exists(path));
    }

    private string Render(
        string fileName,
        CurveAnnotation? annotation,
        IReadOnlyList<CurvePoint>? curve = null
    )
    {
        Directory.CreateDirectory(_directory);

        var path = Path.Combine(_directory, fileName);
        new CurveImageRenderer().SavePng(
            curve ?? Curve(),
            path,
            "试件 S001",
            "跨中挠度 (mm)",
            "荷载 (kN)",
            annotation
        );

        return path;
    }

    private static SKBitmap Decode(string path)
    {
        using var stream = File.OpenRead(path);
        return SKBitmap.Decode(stream);
    }

    private static int CountDifferingPixels(SKBitmap left, SKBitmap right)
    {
        using var leftPixels = left.Copy(SKColorType.Rgba8888);
        using var rightPixels = right.Copy(SKColorType.Rgba8888);

        var leftBytes = leftPixels.GetPixelSpan();
        var rightBytes = rightPixels.GetPixelSpan();
        var differing = 0;

        for (var i = 0; i < leftBytes.Length; i += 4)
        {
            if (leftBytes[i] != rightBytes[i]
                || leftBytes[i + 1] != rightBytes[i + 1]
                || leftBytes[i + 2] != rightBytes[i + 2])
            {
                differing++;
            }
        }

        return differing;
    }

    /// <summary>数"正好是峰值色"的像素（抗锯齿的边缘像素不算，所以只看实心部分）。</summary>
    private static int CountOrangeRed(SKBitmap bitmap)
    {
        using var pixels = bitmap.Copy(SKColorType.Rgba8888);

        var bytes = pixels.GetPixelSpan();
        var count = 0;

        for (var i = 0; i < bytes.Length; i += 4)
        {
            if (bytes[i] == 0xFF && bytes[i + 1] == 0x45 && bytes[i + 2] == 0x00)
            {
                count++;
            }
        }

        return count;
    }
}
