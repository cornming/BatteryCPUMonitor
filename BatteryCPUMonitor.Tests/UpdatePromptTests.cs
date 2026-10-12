using BatteryCPUMonitor.Updates;
using Xunit;

namespace BatteryCPUMonitor.Tests;

public class UpdatePromptTests
{
    private static readonly Version Latest = new(2, 8, 0);

    [Fact]
    public void 自動檢查找到新版本_預設先詢問()
    {
        Assert.Equal(UpdateAction.Ask, UpdatePrompt.Decide(interactive: false, updateWithoutAsking: false, Latest, declined: null));
    }

    [Fact]
    public void 設定成不詢問_自動檢查直接安裝()
    {
        Assert.Equal(UpdateAction.InstallSilently, UpdatePrompt.Decide(interactive: false, updateWithoutAsking: true, Latest, declined: null));
    }

    [Fact]
    public void 手動檢查一律詢問_不管設定怎麼選()
    {
        Assert.Equal(UpdateAction.Ask, UpdatePrompt.Decide(interactive: true, updateWithoutAsking: false, Latest, declined: null));
        Assert.Equal(UpdateAction.Ask, UpdatePrompt.Decide(interactive: true, updateWithoutAsking: true, Latest, declined: null));
    }

    [Fact]
    public void 同一個版本已經回答過不更新_自動檢查不再問()
    {
        Assert.Equal(UpdateAction.Skip, UpdatePrompt.Decide(interactive: false, updateWithoutAsking: false, Latest, declined: new Version(2, 8, 0)));
    }

    [Fact]
    public void 版本號位數不同也視為同一個版本()
    {
        // 2.8.0 與 2.8.0.0 在 .NET 裡並不相等，要先統一再比較。
        Assert.Equal(UpdateAction.Skip, UpdatePrompt.Decide(interactive: false, updateWithoutAsking: false, new Version(2, 8, 0), declined: new Version(2, 8, 0, 0)));
    }

    [Fact]
    public void 更新的版本出現_即使先前回答過不更新舊版本_仍然詢問()
    {
        Assert.Equal(UpdateAction.Ask, UpdatePrompt.Decide(interactive: false, updateWithoutAsking: false, new Version(2, 9, 0), declined: new Version(2, 8, 0)));
    }

    [Fact]
    public void 手動檢查不受先前的回答影響()
    {
        Assert.Equal(UpdateAction.Ask, UpdatePrompt.Decide(interactive: true, updateWithoutAsking: false, Latest, declined: Latest));
    }

    [Fact]
    public void 設定成不詢問時_回答過不更新也不影響()
    {
        // 「不詢問」是使用者明確選的，這時沒有「回答」這回事。
        Assert.Equal(UpdateAction.InstallSilently, UpdatePrompt.Decide(interactive: false, updateWithoutAsking: true, Latest, declined: Latest));
    }
}
