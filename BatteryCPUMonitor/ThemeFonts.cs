using System.Drawing;
using System.Drawing.Text;

namespace BatteryCPUMonitor;

/// <summary>
/// 橫條與工作列使用的多層次字體組：
/// 標籤用輕量無襯線（中文與英文兼顧）、數值用等寬字體（數字寬度穩定不跳動）、單位略小一階。
/// </summary>
internal sealed class ThemeFontSet : IDisposable
{
    private static readonly Lazy<HashSet<string>> InstalledFamilies = new(LoadInstalledFamilies);

    private static readonly string[] LabelCandidates =
    [
        "Microsoft JhengHei UI",
        "Segoe UI Variable Text",
        "Segoe UI",
    ];

    private static readonly string[] MonoCandidates =
    [
        "Cascadia Mono",
        "Consolas",
    ];

    public Font Label { get; }
    public Font Value { get; }
    public Font Unit { get; }
    public Font Micro { get; }
    public int Dpi { get; }
    public float BasePoints { get; }

    private ThemeFontSet(Font label, Font value, Font unit, Font micro, int dpi, float basePoints)
    {
        Label = label;
        Value = value;
        Unit = unit;
        Micro = micro;
        Dpi = dpi;
        BasePoints = basePoints;
    }

    public static string LabelFamilyName => Resolve(LabelCandidates, FontFamily.GenericSansSerif.Name);

    public static string MonoFamilyName => Resolve(MonoCandidates, FontFamily.GenericMonospace.Name);

    public static ThemeFontSet Create(float valuePoints, int dpi)
    {
        float labelPoints = Math.Max(6.5f, valuePoints - 0.85f);
        float unitPoints = Math.Max(6.0f, valuePoints - 1.15f);
        float microPoints = Math.Max(5.5f, valuePoints - 2.6f);

        return new ThemeFontSet(
            label: MakeFont(LabelFamilyName, labelPoints, dpi, FontStyle.Regular),
            value: MakeFont(MonoFamilyName, valuePoints, dpi, FontStyle.Bold),
            unit: MakeFont(MonoFamilyName, unitPoints, dpi, FontStyle.Regular),
            micro: MakeFont(MonoFamilyName, microPoints, dpi, FontStyle.Bold),
            dpi: dpi,
            basePoints: valuePoints);
    }

    public static Font CreateDialogUiFont(float points, FontStyle style = FontStyle.Regular) =>
        new(LabelFamilyName, points, style, GraphicsUnit.Point);

    public static Font CreateDialogMonoFont(float points, FontStyle style = FontStyle.Regular) =>
        new(MonoFamilyName, points, style, GraphicsUnit.Point);

    public void Dispose()
    {
        Label.Dispose();
        Value.Dispose();
        Unit.Dispose();
        Micro.Dispose();
    }

    private static Font MakeFont(string familyName, float points, int dpi, FontStyle style) =>
        new(familyName, points * dpi / 72f, style, GraphicsUnit.Pixel);

    private static string Resolve(IReadOnlyList<string> candidates, string fallback)
    {
        HashSet<string> installed = InstalledFamilies.Value;
        foreach (string candidate in candidates)
        {
            if (installed.Contains(candidate))
            {
                return candidate;
            }
        }

        return fallback;
    }

    private static HashSet<string> LoadInstalledFamilies()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var collection = new InstalledFontCollection();
            foreach (FontFamily family in collection.Families)
            {
                names.Add(family.Name);
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ArgumentException)
        {
            // 無法列舉系統字型時，直接退回系統通用字型。
        }

        return names;
    }
}
