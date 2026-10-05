namespace BatteryCPUMonitor;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 只允許同一位使用者同時執行一份，避免開機啟動後又手動開一次而出現兩條。
        using var mutex = new Mutex(initiallyOwned: true, @"Local\BatteryCPUMonitor.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new BarForm());
    }
}
