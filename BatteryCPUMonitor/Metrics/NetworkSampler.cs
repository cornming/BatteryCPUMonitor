using System.Diagnostics;
using System.Net.NetworkInformation;

namespace BatteryCPUMonitor.Metrics;

/// <summary>網路上傳與下載速度，單位為位元組／秒。</summary>
internal readonly record struct NetworkRate(double UploadBytesPerSecond, double DownloadBytesPerSecond);

/// <summary>
/// 由各網路介面的累計流量算出目前的上傳、下載速度。不需要系統管理員權限。
/// </summary>
/// <remarks>
/// 只計算「有預設閘道」的介面，也就是實際對外連線的那幾張網卡，
/// 避免虛擬交換器把同一份流量算兩次；找不到這種介面時才退而計算所有已連線的介面。
/// </remarks>
internal sealed class NetworkSampler
{
    private Dictionary<string, (long Sent, long Received)> _previous = new();
    private long _previousTimestamp;
    private bool _hasPrevious;

    /// <summary>回傳上一次呼叫到這一次呼叫之間的平均速度；第一次呼叫或讀取失敗時回傳 null。</summary>
    public NetworkRate? Sample()
    {
        Dictionary<string, (long Sent, long Received)> current;
        try
        {
            current = ReadTotals();
        }
        catch (Exception)
        {
            // 列舉網路介面偶爾會因為介面正在變動而失敗；監控工具不該因此當掉，這一次先不顯示。
            _hasPrevious = false;
            return null;
        }

        long now = Stopwatch.GetTimestamp();
        NetworkRate? rate = null;

        if (_hasPrevious)
        {
            double seconds = Stopwatch.GetElapsedTime(_previousTimestamp, now).TotalSeconds;
            (long sent, long received) = NetworkMath.SumDeltas(_previous, current);
            rate = NetworkMath.ToRate(sent, received, seconds);
        }

        (_previous, _previousTimestamp, _hasPrevious) = (current, now, true);
        return rate;
    }

    private static Dictionary<string, (long Sent, long Received)> ReadTotals()
    {
        var candidates = new List<(NetworkInterface Nic, bool HasGateway)>();

        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            candidates.Add((nic, HasGateway(nic)));
        }

        bool anyGateway = candidates.Any(c => c.HasGateway);
        var totals = new Dictionary<string, (long Sent, long Received)>();

        foreach ((NetworkInterface nic, bool hasGateway) in candidates)
        {
            if (anyGateway && !hasGateway)
            {
                continue;
            }

            IPInterfaceStatistics stats = nic.GetIPStatistics();
            totals[nic.Id] = (stats.BytesSent, stats.BytesReceived);
        }

        return totals;
    }

    private static bool HasGateway(NetworkInterface nic)
    {
        try
        {
            return nic.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(System.Net.IPAddress.Any)
                                                                 && !g.Address.Equals(System.Net.IPAddress.IPv6Any));
        }
        catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}

internal static class NetworkMath
{
    /// <summary>
    /// 加總兩次取樣之間各介面的流量差。只計算兩次都存在的介面，
    /// 這樣中途新連上的介面（例如 VPN）不會把它過去累計的流量一次灌進來；計數器歸零造成的負值視為 0。
    /// </summary>
    public static (long Sent, long Received) SumDeltas(
        IReadOnlyDictionary<string, (long Sent, long Received)> previous,
        IReadOnlyDictionary<string, (long Sent, long Received)> current)
    {
        long sent = 0;
        long received = 0;

        foreach ((string id, (long Sent, long Received) now) in current)
        {
            if (previous.TryGetValue(id, out (long Sent, long Received) before))
            {
                sent += Math.Max(0, now.Sent - before.Sent);
                received += Math.Max(0, now.Received - before.Received);
            }
        }

        return (sent, received);
    }

    public static NetworkRate? ToRate(long sentBytes, long receivedBytes, double seconds) =>
        seconds > 0 ? new NetworkRate(sentBytes / seconds, receivedBytes / seconds) : null;
}
