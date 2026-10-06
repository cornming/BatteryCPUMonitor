using BatteryCPUMonitor.Updates;

namespace BatteryCPUMonitor;

internal static class Program
{
    private const string SingleInstanceName = @"Local\BatteryCPUMonitor.SingleInstance";

    /// <summary>更新後接手的新版本最多等舊版本結束這麼久。</summary>
    private static readonly TimeSpan HandoverTimeout = TimeSpan.FromSeconds(20);

    [STAThread]
    private static int Main(string[] args)
    {
        // 更新時用來試跑新版本：寫完報告就結束，不碰任何畫面或單一執行個體的機制。
        if (SelfCheckRunner.TryHandle(args, out int selfCheckExitCode))
        {
            return selfCheckExitCode;
        }

        bool afterUpdate = args.Contains(UpdateRelauncher.AfterUpdateArgument);

        // 只允許同一位使用者同時執行一份，避免開機啟動後又手動開一次而出現兩條。
        // 更新後的新版本是在舊版本還沒結束時啟動的，所以這種情況要等舊版本放手。
        using var mutex = new Mutex(initiallyOwned: false, SingleInstanceName);
        if (!TryBecomeOnlyInstance(mutex, afterUpdate ? HandoverTimeout : TimeSpan.Zero))
        {
            return 0;
        }

        // 上次更新換下來的舊檔案，這時舊版本已經結束，可以刪了。
        UpdateInstaller.DeleteLeftovers(Environment.ProcessPath);

        ApplicationConfiguration.Initialize();
        Application.Run(new BarForm(afterUpdate));
        return 0;
    }

    private static bool TryBecomeOnlyInstance(Mutex mutex, TimeSpan wait)
    {
        try
        {
            return mutex.WaitOne(wait);
        }
        catch (AbandonedMutexException)
        {
            // 前一個行程沒有正常放手就結束了（例如被強制關閉）；現在這個 Mutex 已經歸我們所有。
            return true;
        }
    }
}
