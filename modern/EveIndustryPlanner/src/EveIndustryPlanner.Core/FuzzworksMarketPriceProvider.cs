using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EveIndustryPlanner.Core;

public sealed class FuzzworksMarketPriceProvider : IMarketPriceProvider
{
    public const long TheForgeRegionId = 10000002;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        WriteIndented = true
    };

    private readonly HttpClient httpClient;
    private readonly string cacheFilePath;
    private readonly long regionId;
    private readonly TimeSpan cacheDuration;
    private readonly SemaphoreSlim cacheLock = new(1, 1);
    private Dictionary<string, CachedMarketPrice>? cache;

    public FuzzworksMarketPriceProvider(
        string cacheFilePath,
        HttpClient? httpClient = null,
        long regionId = TheForgeRegionId,
        TimeSpan? cacheDuration = null)
    {
        this.cacheFilePath = cacheFilePath;
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        this.regionId = regionId;
        this.cacheDuration = cacheDuration ?? TimeSpan.FromMinutes(30);
    }

    public async Task<MarketPrice?> GetPriceAsync(TypeId typeId, PriceProfile profile, CancellationToken cancellationToken)
    {
        var locationId = profile.MarketLocationId >= 0 ? profile.MarketLocationId : regionId;

        await cacheLock.WaitAsync(cancellationToken);
        try
        {
            cache ??= await LoadCacheAsync(cancellationToken);
            var cacheKey = GetCacheKey(typeId.Value, locationId);
            cache.TryGetValue(cacheKey, out var cachedPrice);

            if (cachedPrice is not null && DateTimeOffset.UtcNow - cachedPrice.FetchedAt <= cacheDuration)
            {
                return cachedPrice.ToMarketPrice();
            }

            try
            {
                var downloaded = await DownloadPriceAsync(typeId, locationId, cancellationToken);
                if (downloaded is null)
                {
                    return cachedPrice?.ToMarketPrice();
                }

                cache[cacheKey] = downloaded;
                await SaveCacheAsync(cache, cancellationToken);
                return downloaded.ToMarketPrice();
            }
            catch (HttpRequestException)
            {
                return cachedPrice?.ToMarketPrice();
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return cachedPrice?.ToMarketPrice();
            }
            catch (JsonException)
            {
                return cachedPrice?.ToMarketPrice();
            }
        }
        finally
        {
            cacheLock.Release();
        }
    }

    private async Task<CachedMarketPrice?> DownloadPriceAsync(TypeId typeId, long locationId, CancellationToken cancellationToken)
    {
        var url = $"https://market.fuzzwork.co.uk/aggregates/?region={locationId}&types={typeId.Value}";
        var response = await httpClient.GetFromJsonAsync<Dictionary<string, FuzzworksTypePrice>>(url, JsonOptions, cancellationToken);
        if (response is null || !response.TryGetValue(typeId.Value.ToString(), out var price))
        {
            return null;
        }

        if (price.Buy.Max <= 0 && price.Sell.Min <= 0)
        {
            return null;
        }

        return new CachedMarketPrice
        {
            TypeId = typeId.Value,
            RegionId = locationId,
            BuyPrice = price.Buy.Max,
            SellPrice = price.Sell.Min,
            BuyWeightedAveragePrice = price.Buy.WeightedAverage,
            BuyMaxPrice = price.Buy.Max,
            BuyMinPrice = price.Buy.Min,
            BuyMedianPrice = price.Buy.Median,
            BuyPercentilePrice = price.Buy.Percentile,
            SellWeightedAveragePrice = price.Sell.WeightedAverage,
            SellMaxPrice = price.Sell.Max,
            SellMinPrice = price.Sell.Min,
            SellMedianPrice = price.Sell.Median,
            SellPercentilePrice = price.Sell.Percentile,
            FetchedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task<Dictionary<string, CachedMarketPrice>> LoadCacheAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(cacheFilePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(cacheFilePath);
        var loaded = await JsonSerializer.DeserializeAsync<Dictionary<string, CachedMarketPrice>>(stream, JsonOptions, cancellationToken);
        return loaded ?? [];
    }

    private async Task SaveCacheAsync(Dictionary<string, CachedMarketPrice> prices, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(cacheFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(cacheFilePath);
        await JsonSerializer.SerializeAsync(stream, prices, JsonOptions, cancellationToken);
    }

    private static string GetCacheKey(long typeId, long regionId)
    {
        return $"{regionId}:{typeId}";
    }

    private sealed class CachedMarketPrice
    {
        public long TypeId { get; init; }
        public long RegionId { get; init; }
        public decimal BuyPrice { get; init; }
        public decimal SellPrice { get; init; }
        public decimal BuyWeightedAveragePrice { get; init; }
        public decimal BuyMaxPrice { get; init; }
        public decimal BuyMinPrice { get; init; }
        public decimal BuyMedianPrice { get; init; }
        public decimal BuyPercentilePrice { get; init; }
        public decimal SellWeightedAveragePrice { get; init; }
        public decimal SellMaxPrice { get; init; }
        public decimal SellMinPrice { get; init; }
        public decimal SellMedianPrice { get; init; }
        public decimal SellPercentilePrice { get; init; }
        public DateTimeOffset FetchedAt { get; init; }

        public MarketPrice ToMarketPrice()
        {
            return new MarketPrice
            {
                TypeId = new TypeId(TypeId),
                BuyPrice = BuyPrice,
                SellPrice = SellPrice,
                BuyWeightedAveragePrice = BuyWeightedAveragePrice,
                BuyMaxPrice = BuyMaxPrice,
                BuyMinPrice = BuyMinPrice,
                BuyMedianPrice = BuyMedianPrice,
                BuyPercentilePrice = BuyPercentilePrice,
                SellWeightedAveragePrice = SellWeightedAveragePrice,
                SellMaxPrice = SellMaxPrice,
                SellMinPrice = SellMinPrice,
                SellMedianPrice = SellMedianPrice,
                SellPercentilePrice = SellPercentilePrice,
                AsOf = FetchedAt
            };
        }
    }

    private sealed class FuzzworksTypePrice
    {
        [JsonPropertyName("buy")]
        public FuzzworksStat Buy { get; init; } = new();

        [JsonPropertyName("sell")]
        public FuzzworksStat Sell { get; init; } = new();
    }

    private sealed class FuzzworksStat
    {
        [JsonPropertyName("weightedAverage")]
        public decimal WeightedAverage { get; init; }

        [JsonPropertyName("max")]
        public decimal Max { get; init; }

        [JsonPropertyName("min")]
        public decimal Min { get; init; }

        [JsonPropertyName("median")]
        public decimal Median { get; init; }

        [JsonPropertyName("percentile")]
        public decimal Percentile { get; init; }
    }
}
