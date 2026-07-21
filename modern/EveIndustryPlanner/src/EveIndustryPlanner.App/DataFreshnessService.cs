using System.IO;

namespace EveIndustryPlanner.App;

public sealed record DataFreshnessSnapshot(string SdeText, string MarketText, string CostIndexText);

public sealed class DataFreshnessService(string sdeDirectory, string marketCachePath, string costIndexCachePath)
{
    public DataFreshnessSnapshot GetSnapshot()
    {
        return new DataFreshnessSnapshot(
            FormatFile("SDE", Path.Combine(sdeDirectory, "blueprints.yaml"), TimeSpan.FromDays(30)),
            FormatFile("Market", marketCachePath, TimeSpan.FromHours(24)),
            FormatFile("Cost index", costIndexCachePath, TimeSpan.FromDays(1)));
    }

    private static string FormatFile(string label, string path, TimeSpan staleAfter)
    {
        if (!File.Exists(path))
        {
            return $"{label}: not cached";
        }

        var timestamp = File.GetLastWriteTimeUtc(path);
        var age = DateTime.UtcNow - timestamp;
        var stale = age > staleAfter ? " (stale)" : string.Empty;
        return $"{label}: {timestamp.ToLocalTime():g}{stale}";
    }
}
