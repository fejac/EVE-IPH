using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EveIndustryPlanner.Core;

public sealed class EsiIndustryCostIndexProvider : IIndustryCostIndexProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        WriteIndented = true
    };

    private readonly HttpClient httpClient;
    private readonly string cacheFilePath;
    private readonly TimeSpan cacheDuration;
    private readonly SemaphoreSlim cacheLock = new(1, 1);
    private CachedIndustryCostIndexes? cache;

    public EsiIndustryCostIndexProvider(
        string cacheFilePath,
        HttpClient? httpClient = null,
        TimeSpan? cacheDuration = null)
    {
        this.cacheFilePath = cacheFilePath;
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        this.cacheDuration = cacheDuration ?? TimeSpan.FromHours(6);
    }

    public async Task<IndustryCostIndex?> GetCostIndexAsync(long solarSystemId, FacilityServiceRole serviceRole, CancellationToken cancellationToken)
    {
        await cacheLock.WaitAsync(cancellationToken);
        try
        {
            cache ??= await LoadCacheAsync(cancellationToken);
            if (DateTimeOffset.UtcNow - cache.FetchedAt > cacheDuration)
            {
                try
                {
                    cache = await DownloadCostIndexesAsync(cancellationToken);
                    await SaveCacheAsync(cache, cancellationToken);
                }
                catch (HttpRequestException)
                {
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                }
                catch (JsonException)
                {
                }
            }

            var activity = ToEsiActivity(serviceRole);
            if (!cache.Systems.TryGetValue(solarSystemId.ToString(), out var system)
                || !system.TryGetValue(activity, out var costIndex))
            {
                return null;
            }

            return new IndustryCostIndex(solarSystemId, serviceRole, costIndex, cache.FetchedAt);
        }
        finally
        {
            cacheLock.Release();
        }
    }

    private async Task<CachedIndustryCostIndexes> DownloadCostIndexesAsync(CancellationToken cancellationToken)
    {
        var url = "https://esi.evetech.net/latest/industry/systems/?datasource=tranquility";
        var systems = await httpClient.GetFromJsonAsync<List<EsiIndustrySystem>>(url, JsonOptions, cancellationToken);
        var cacheData = new CachedIndustryCostIndexes { FetchedAt = DateTimeOffset.UtcNow };

        foreach (var system in systems ?? [])
        {
            cacheData.Systems[system.SolarSystemId.ToString()] = system.CostIndices
                .ToDictionary(index => index.Activity, index => index.CostIndex);
        }

        return cacheData;
    }

    private async Task<CachedIndustryCostIndexes> LoadCacheAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(cacheFilePath))
        {
            return new CachedIndustryCostIndexes { FetchedAt = DateTimeOffset.MinValue };
        }

        await using var stream = File.OpenRead(cacheFilePath);
        return await JsonSerializer.DeserializeAsync<CachedIndustryCostIndexes>(stream, JsonOptions, cancellationToken)
            ?? new CachedIndustryCostIndexes { FetchedAt = DateTimeOffset.MinValue };
    }

    private async Task SaveCacheAsync(CachedIndustryCostIndexes costIndexes, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(cacheFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(cacheFilePath);
        await JsonSerializer.SerializeAsync(stream, costIndexes, JsonOptions, cancellationToken);
    }

    private static string ToEsiActivity(FacilityServiceRole serviceRole)
    {
        return serviceRole == FacilityServiceRole.Reactions
            ? "reaction"
            : "manufacturing";
    }

    private sealed class CachedIndustryCostIndexes
    {
        public DateTimeOffset FetchedAt { get; init; }
        public Dictionary<string, Dictionary<string, decimal>> Systems { get; init; } = [];
    }

    private sealed class EsiIndustrySystem
    {
        [JsonPropertyName("solar_system_id")]
        public long SolarSystemId { get; init; }

        [JsonPropertyName("cost_indices")]
        public List<EsiCostIndex> CostIndices { get; init; } = [];
    }

    private sealed class EsiCostIndex
    {
        [JsonPropertyName("activity")]
        public string Activity { get; init; } = string.Empty;

        [JsonPropertyName("cost_index")]
        public decimal CostIndex { get; init; }
    }
}
