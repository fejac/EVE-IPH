using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EveIndustryPlanner.Core;

public sealed class FuzzworksMarketPriceProvider : IMarketPriceProvider, IMarketOrderBookProvider
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
    private readonly TimeSpan orderBookCacheDuration = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim cacheLock = new(1, 1);
    private Dictionary<string, CachedMarketPrice>? cache;
    private readonly Dictionary<string, CachedMarketOrders> orderBookCache = [];

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

    public async Task<EffectiveMarketPrice?> GetEffectivePriceAsync(TypeId typeId, PriceProfile profile, MarketPriceSelection selection, long quantity, CancellationToken cancellationToken)
    {
        if (selection != MarketPriceSelection.InstantBuy || quantity <= 0)
        {
            return null;
        }

        var scope = MarketLocationScope.From(profile.MarketLocationId, regionId);
        if (scope is null)
        {
            return null;
        }

        try
        {
            var orders = await DownloadMarketOrdersAsync(typeId, scope, cancellationToken);
            var remaining = quantity;
            var totalPrice = 0m;
            var filledQuantity = 0L;

            foreach (var order in orders.OrderBy(order => order.Price))
            {
                var usedQuantity = Math.Min(remaining, order.VolumeRemaining);
                totalPrice += usedQuantity * order.Price;
                filledQuantity += usedQuantity;
                remaining -= usedQuantity;

                if (remaining <= 0)
                {
                    break;
                }
            }

            if (filledQuantity == 0)
            {
                return null;
            }

            var pricedQuantity = Math.Min(quantity, filledQuantity);
            return new EffectiveMarketPrice
            {
                UnitPrice = totalPrice / pricedQuantity,
                TotalPrice = totalPrice,
                FilledQuantity = filledQuantity,
                RequestedQuantity = quantity
            };
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
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

    private async Task<List<EsiMarketOrder>> DownloadMarketOrdersAsync(TypeId typeId, MarketLocationScope scope, CancellationToken cancellationToken)
    {
        var cacheKey = $"{scope.RegionId}:{scope.SystemId}:{scope.LocationId}:{typeId.Value}:sell";
        if (orderBookCache.TryGetValue(cacheKey, out var cachedOrders) && DateTimeOffset.UtcNow - cachedOrders.FetchedAt <= orderBookCacheDuration)
        {
            return cachedOrders.Orders;
        }

        var orders = new List<EsiMarketOrder>();
        var page = 1;
        var totalPages = 1;

        do
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://esi.evetech.net/latest/markets/{scope.RegionId}/orders/?datasource=tranquility&order_type=sell&type_id={typeId.Value}&page={page}");
            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            if (page == 1 && response.Headers.TryGetValues("X-Pages", out var values) && int.TryParse(values.FirstOrDefault(), out var parsedPages))
            {
                totalPages = Math.Max(1, parsedPages);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var pageOrders = await JsonSerializer.DeserializeAsync<List<EsiMarketOrder>>(stream, JsonOptions, cancellationToken);
            if (pageOrders is not null)
            {
                orders.AddRange(pageOrders.Where(scope.Contains));
            }

            page++;
        }
        while (page <= totalPages);

        orderBookCache[cacheKey] = new CachedMarketOrders(orders, DateTimeOffset.UtcNow);
        return orders;
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

    private sealed class EsiMarketOrder
    {
        [JsonPropertyName("location_id")]
        public long LocationId { get; init; }

        [JsonPropertyName("system_id")]
        public long SystemId { get; init; }

        [JsonPropertyName("volume_remain")]
        public long VolumeRemaining { get; init; }

        [JsonPropertyName("price")]
        public decimal Price { get; init; }
    }

    private sealed record CachedMarketOrders(List<EsiMarketOrder> Orders, DateTimeOffset FetchedAt);

    private sealed record MarketLocationScope(long RegionId, long? SystemId, long? LocationId)
    {
        public bool Contains(EsiMarketOrder order)
        {
            if (LocationId is not null)
            {
                return order.LocationId == LocationId.Value;
            }

            if (SystemId is not null)
            {
                return order.SystemId == SystemId.Value;
            }

            return true;
        }

        public static MarketLocationScope? From(long locationId, long defaultRegionId)
        {
            if (locationId <= 0)
            {
                return new MarketLocationScope(defaultRegionId, null, null);
            }

            if (locationId is >= 10_000_000 and < 20_000_000)
            {
                return new MarketLocationScope(locationId, null, null);
            }

            return locationId switch
            {
                30000142 => new MarketLocationScope(TheForgeRegionId, 30000142, null),
                30000144 => new MarketLocationScope(TheForgeRegionId, 30000144, null),
                60003760 => new MarketLocationScope(TheForgeRegionId, null, 60003760),
                60008494 => new MarketLocationScope(10000043, null, 60008494),
                60011866 => new MarketLocationScope(10000032, null, 60011866),
                60004588 => new MarketLocationScope(10000030, null, 60004588),
                60005686 => new MarketLocationScope(10000042, null, 60005686),
                _ => null
            };
        }
    }
}
