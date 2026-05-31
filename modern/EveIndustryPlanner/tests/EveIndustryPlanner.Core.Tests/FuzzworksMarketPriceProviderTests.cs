using System.Net;
using EveIndustryPlanner.Core;

namespace EveIndustryPlanner.Core.Tests;

public sealed class FuzzworksMarketPriceProviderTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(Path.GetTempPath(), $"eip-market-{Guid.NewGuid():N}");

    public FuzzworksMarketPriceProviderTests()
    {
        Directory.CreateDirectory(tempDirectory);
    }

    [Fact]
    public async Task GetPriceAsync_MapsFuzzworksBuyMaxAndSellMin()
    {
        var handler = new StaticJsonHandler("""
            {
              "34": {
                "buy": { "weightedAverage": "5.10", "max": "5.50", "min": "4.00", "median": "5.25", "percentile": "5.40" },
                "sell": { "weightedAverage": "6.80", "max": "7.00", "min": "6.25", "median": "6.60", "percentile": "6.35" }
              }
            }
            """);
        var provider = CreateProvider(handler);

        var price = await provider.GetPriceAsync(new TypeId(34), PriceProfile.Empty, CancellationToken.None);

        Assert.NotNull(price);
        Assert.Equal(5.50m, price.BuyPrice);
        Assert.Equal(6.25m, price.SellPrice);
        Assert.Equal(5.25m, price.Select(MarketPriceSelection.BuyMedian));
        Assert.Equal(6.35m, price.Select(MarketPriceSelection.SellPercentile));
        Assert.Contains("region=10000002", handler.Requests.Single().Query);
        Assert.Contains("types=34", handler.Requests.Single().Query);
    }

    [Fact]
    public async Task GetPriceAsync_UsesLocationFromPriceProfile()
    {
        var handler = new StaticJsonHandler("""
            {
              "34": {
                "buy": { "max": "5.50", "min": "4.00" },
                "sell": { "max": "7.00", "min": "6.25" }
              }
            }
            """);
        var provider = CreateProvider(handler);

        await provider.GetPriceAsync(new TypeId(34), new PriceProfile { MarketLocationId = 60003760 }, CancellationToken.None);

        Assert.Contains("region=60003760", handler.Requests.Single().Query);
    }

    [Fact]
    public async Task GetPriceAsync_UsesCacheForFreshPrice()
    {
        var handler = new StaticJsonHandler("""
            {
              "34": {
                "buy": { "max": "5.50", "min": "4.00" },
                "sell": { "max": "7.00", "min": "6.25" }
              }
            }
            """);
        var provider = CreateProvider(handler);

        var first = await provider.GetPriceAsync(new TypeId(34), PriceProfile.Empty, CancellationToken.None);
        var second = await provider.GetPriceAsync(new TypeId(34), PriceProfile.Empty, CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.BuyPrice, second.BuyPrice);
        Assert.Single(handler.Requests);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private FuzzworksMarketPriceProvider CreateProvider(HttpMessageHandler handler)
    {
        return new FuzzworksMarketPriceProvider(
            Path.Combine(tempDirectory, "prices.json"),
            new HttpClient(handler),
            cacheDuration: TimeSpan.FromMinutes(30));
    }

    private sealed class StaticJsonHandler(string json) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            };

            return Task.FromResult(response);
        }
    }
}
