namespace BatteryCPUMonitor.Updates;

internal enum UpdateAction
{
    /// <summary>什麼都不做。</summary>
    Skip,

    /// <summary>先顯示更新內容並詢問，使用者同意才下載安裝。</summary>
    Ask,

    /// <summary>不詢問，直接在背景下載安裝。</summary>
    InstallSilently,
}

/// <summary>找到新版本之後，要詢問使用者、直接安裝，還是不打擾。純計算。</summary>
internal static class UpdatePrompt
{
    /// <param name="interactive">使用者自己按了「檢查更新…」。這時一定要詢問，而且不受先前的回答影響。</param>
    /// <param name="updateWithoutAsking">設定為「不詢問、直接更新」。</param>
    /// <param name="latest">找到的新版本。</param>
    /// <param name="declined">這次執行期間，使用者已經回答過「不更新」的版本；沒有為 null。</param>
    public static UpdateAction Decide(bool interactive, bool updateWithoutAsking, Version latest, Version? declined)
    {
        if (interactive)
        {
            return UpdateAction.Ask;
        }

        if (updateWithoutAsking)
        {
            return UpdateAction.InstallSilently;
        }

        // 自動檢查每 12 小時一次，同一個版本已經回答過「不更新」就不要一直問；有更新的版本出現時才會再問。
        bool alreadyDeclined = declined is not null && AppVersion.Normalize(declined) == AppVersion.Normalize(latest);
        return alreadyDeclined ? UpdateAction.Skip : UpdateAction.Ask;
    }
}
