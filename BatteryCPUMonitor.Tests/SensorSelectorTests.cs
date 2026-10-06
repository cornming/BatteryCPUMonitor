using BatteryCPUMonitor.Sensors;
using Xunit;

namespace BatteryCPUMonitor.Tests;

public class SensorSelectorTests
{
    private static SensorReading Temp(string hardware, string name, double value) => new(hardware, "Temperature", name, value);

    private static SensorReading Power(string hardware, string name, double value) => new(hardware, "Power", name, value);

    private static SensorReading Clock(string hardware, string name, double value) => new(hardware, "Clock", name, value);

    private static SensorReading Fan(string name, double rpm, string hardware = "SuperIO") => new(hardware, "Fan", name, rpm);

    // ---- CPU ----

    [Fact]
    public void Intel_CPU溫度優先採用Package()
    {
        SensorValues values = SensorSelector.Select(
        [
            Temp("Cpu", "Core #1", 61), Temp("Cpu", "Core #2", 64), Temp("Cpu", "Core Max", 65), Temp("Cpu", "CPU Package", 66),
        ]);

        Assert.Equal(66.0, values.CpuTemperature);
    }

    [Fact]
    public void AMD_CPU溫度採用Tctl_Tdie()
    {
        SensorValues values = SensorSelector.Select([Temp("Cpu", "Core (Tctl/Tdie)", 58), Temp("Cpu", "CCD1 (Tdie)", 55)]);

        Assert.Equal(58.0, values.CpuTemperature);
    }

    [Fact]
    public void 沒有Package時改用Core_Max_再退而求其次取最高的核心()
    {
        Assert.Equal(70.0, SensorSelector.Select([Temp("Cpu", "Core #1", 60), Temp("Cpu", "Core Max", 70)]).CpuTemperature);
        Assert.Equal(64.0, SensorSelector.Select([Temp("Cpu", "Core #1", 60), Temp("Cpu", "Core #2", 64)]).CpuTemperature);
    }

    [Fact]
    public void 主機板上的溫度不會被當成CPU溫度()
    {
        SensorValues values = SensorSelector.Select([Temp("SuperIO", "CPU", 45), Temp("Motherboard", "System", 38)]);

        Assert.Null(values.CpuTemperature);
    }

    [Fact]
    public void CPU功耗優先採用Package_沒有時用CPU_Cores()
    {
        Assert.Equal(35.5, SensorSelector.Select([Power("Cpu", "CPU Cores", 28), Power("Cpu", "CPU Package", 35.5)]).CpuPowerWatts);
        Assert.Equal(28.0, SensorSelector.Select([Power("Cpu", "CPU Cores", 28), Power("Cpu", "CPU Memory", 3)]).CpuPowerWatts);
    }

    // ---- GPU ----

    [Fact]
    public void GPU溫度採用GPU_Core_而不是熱點()
    {
        SensorValues values = SensorSelector.Select([Temp("GpuNvidia", "GPU Hot Spot", 82), Temp("GpuNvidia", "GPU Core", 67)]);

        Assert.Equal(67.0, values.GpuTemperature);
    }

    [Fact]
    public void 同時有內顯與獨顯_採用獨立顯示卡()
    {
        SensorValues values = SensorSelector.Select(
        [
            Temp("GpuIntel", "GPU Core", 50), Clock("GpuIntel", "GPU Core", 300),
            Temp("GpuNvidia", "GPU Core", 71), Clock("GpuNvidia", "GPU Core", 1800), Power("GpuNvidia", "GPU Package", 120),
        ]);

        Assert.Equal(71.0, values.GpuTemperature);
        Assert.Equal(1800.0, values.GpuClockMHz);
        Assert.Equal(120.0, values.GpuPowerWatts);
    }

    [Fact]
    public void 只有內顯時採用內顯()
    {
        Assert.Equal(48.0, SensorSelector.Select([Temp("GpuIntel", "GPU Core", 48)]).GpuTemperature);
    }

    [Fact]
    public void 沒有任何GPU感測器_GPU數值為空()
    {
        SensorValues values = SensorSelector.Select([Temp("Cpu", "CPU Package", 50)]);

        Assert.Null(values.GpuTemperature);
        Assert.Null(values.GpuPowerWatts);
        Assert.Null(values.GpuClockMHz);
    }

    // ---- 風扇 ----

    [Fact]
    public void 風扇_CPU風扇排最前面_其餘依轉速由高到低()
    {
        SensorValues values = SensorSelector.Select([Fan("Fan #2", 900), Fan("CPU Fan", 1200), Fan("Fan #1", 1500)]);

        Assert.Equal(
            new[] { new FanReading("CPU風扇", 1200), new FanReading("風扇2", 1500), new FanReading("風扇3", 900) },
            values.Fans);
    }

    [Fact]
    public void 轉速為零的插座_沒接風扇_不列入()
    {
        SensorValues values = SensorSelector.Select([Fan("Fan #1", 0), Fan("Fan #2", 800)]);

        Assert.Equal(new[] { new FanReading("風扇1", 800) }, values.Fans);
    }

    [Fact]
    public void 顯示卡風扇標示為GPU風扇()
    {
        SensorValues values = SensorSelector.Select([Fan("GPU Fan", 1400, "GpuNvidia")]);

        Assert.Equal("GPU風扇", Assert.Single(values.Fans).Label);
    }

    [Theory]
    [InlineData("CPU Fan", 1, "CPU風扇")]
    [InlineData("CPU_OPT", 2, "CPU風扇")]
    [InlineData("GPU Fan", 1, "GPU風扇")]
    [InlineData("Fan #3", 2, "風扇2")]
    [InlineData("System Fan", 3, "風扇3")]
    public void 風扇標籤(string name, int position, string expected)
    {
        Assert.Equal(expected, SensorSelector.FanLabel(name, position));
    }

    // ---- 不合理的數值 ----

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(255.0)]
    [InlineData(1000.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void 不合理的溫度會被丟棄(double value)
    {
        Assert.Null(SensorSelector.Select([Temp("Cpu", "CPU Package", value)]).CpuTemperature);
    }

    [Fact]
    public void 一個感測器壞掉_不影響其他感測器()
    {
        SensorValues values = SensorSelector.Select([Temp("Cpu", "CPU Package", 255), Temp("Cpu", "Core #1", 57)]);

        Assert.Equal(57.0, values.CpuTemperature);
    }

    [Fact]
    public void 不合理的功耗_與頻率會被丟棄()
    {
        SensorValues values = SensorSelector.Select([Power("Cpu", "CPU Package", 99999), Clock("GpuNvidia", "GPU Core", 0), Clock("GpuAmd", "GPU Core", 99999)]);

        Assert.Null(values.CpuPowerWatts);
        Assert.Null(values.GpuClockMHz);
    }

    [Fact]
    public void 功耗為零視為沒有讀到_沒裝驅動程式時函式庫回報的是零()
    {
        SensorValues values = SensorSelector.Select([Power("Cpu", "CPU Package", 0), Power("GpuNvidia", "GPU Package", 0)]);

        Assert.Null(values.CpuPowerWatts);
        Assert.Null(values.GpuPowerWatts);
    }

    [Fact]
    public void 零功耗的感測器不會擋住有數值的備選()
    {
        SensorValues values = SensorSelector.Select([Power("Cpu", "CPU Package", 0), Power("Cpu", "CPU Cores", 18)]);

        Assert.Equal(18.0, values.CpuPowerWatts);
    }

    [Fact]
    public void 不認得的種類會被忽略()
    {
        SensorValues values = SensorSelector.Select([new SensorReading("Cpu", "Voltage", "Core", 1.1)]);

        Assert.False(values.HasAnything);
    }

    [Fact]
    public void 沒有任何讀數_什麼都沒有()
    {
        SensorValues values = SensorSelector.Select([]);

        Assert.False(values.HasAnything);
        Assert.Empty(values.Fans);
    }

    [Fact]
    public void 有任何一項數值就算有資料()
    {
        Assert.True(SensorSelector.Select([Temp("Cpu", "CPU Package", 50)]).HasAnything);
        Assert.True(SensorSelector.Select([Fan("Fan #1", 900)]).HasAnything);
    }

    [Fact]
    public void 同一批數值每次挑出來的結果相同_不會每秒跳動()
    {
        SensorReading[] readings = [Fan("Fan #2", 900), Fan("Fan #1", 900), Temp("GpuNvidia", "GPU Core", 60)];

        Assert.Equal(SensorSelector.Select(readings).Fans, SensorSelector.Select(readings.Reverse()).Fans);
    }
}
