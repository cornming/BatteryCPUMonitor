namespace BatteryCPUMonitor.Metrics;

/// <summary>把 Windows「GPU Engine」「GPU Adapter Memory」計數器的原始資料整理成單一數值。純計算。</summary>
internal static class GpuMath
{
    /// <summary>
    /// GPU 使用率（0–100）。做法與工作管理員相同：
    /// 每個「引擎」（3D、影片解碼……）的使用率是所有程式在該引擎上的用量加總，整體取最忙的那個引擎。
    /// </summary>
    /// <param name="engineSamples">
    /// 執行個體名稱形如 <c>pid_1234_luid_0x00000000_0x0000ABCD_phys_0_eng_3_engtype_VideoDecode</c>。
    /// </param>
    public static double? Utilization(IEnumerable<(string Instance, double Value)>? engineSamples)
    {
        if (engineSamples is null)
        {
            return null;
        }

        var perEngine = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach ((string instance, double value) in engineSamples)
        {
            string? engine = EngineKey(instance);
            if (engine is not null)
            {
                perEngine[engine] = perEngine.GetValueOrDefault(engine) + Math.Max(0, value);
            }
        }

        return perEngine.Count == 0 ? null : Math.Clamp(perEngine.Values.Max(), 0, 100);
    }

    /// <summary>
    /// 專用顯示記憶體的使用量（位元組）。有多張顯示卡時取用量最大的那一張。
    /// </summary>
    public static double? DedicatedMemoryBytes(IEnumerable<(string Instance, double Value)>? adapterSamples)
    {
        if (adapterSamples is null)
        {
            return null;
        }

        double? max = null;
        foreach ((_, double value) in adapterSamples)
        {
            if (value >= 0 && (max is null || value > max))
            {
                max = value;
            }
        }

        return max;
    }

    /// <summary>
    /// 取出「哪張顯示卡的哪個引擎」作為分組依據：去掉開頭的程式編號與結尾的引擎類型，
    /// 例如 <c>luid_0x00000000_0x0000ABCD_phys_0_eng_3</c>。名稱格式不符時回傳 null。
    /// </summary>
    internal static string? EngineKey(string instance)
    {
        int start = instance.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        int end = instance.IndexOf("_engtype_", start, StringComparison.OrdinalIgnoreCase);
        return end < 0 ? instance[start..] : instance[start..end];
    }
}
