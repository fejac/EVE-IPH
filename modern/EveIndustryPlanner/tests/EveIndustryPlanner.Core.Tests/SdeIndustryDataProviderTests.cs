using EveIndustryPlanner.Core;

namespace EveIndustryPlanner.Core.Tests;

public sealed class SdeIndustryDataProviderTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(Path.GetTempPath(), $"eip-sde-{Guid.NewGuid():N}");

    public SdeIndustryDataProviderTests()
    {
        Directory.CreateDirectory(tempDirectory);
        File.WriteAllText(Path.Combine(tempDirectory, "blueprints.yaml"), """
            100:
              activities:
                manufacturing:
                  materials:
                  - quantity: 10
                    typeID: 34
                  products:
                  - quantity: 1
                    typeID: 200
                  time: 60
              blueprintTypeID: 100
              maxProductionLimit: 300
            """);
        File.WriteAllText(Path.Combine(tempDirectory, "types.yaml"), """
            34:
              basePrice: 5.0
              name:
                en: Tritanium
              portionSize: 1
              published: true
              volume: 0.01
            100:
              basePrice: 1000.0
              name:
                en: Test Blueprint
              portionSize: 1
              published: true
              volume: 0.01
            200:
              basePrice: 5000.0
              name:
                en: Test Product
              portionSize: 1
              published: true
              volume: 5.0
            """);
    }

    [Fact]
    public async Task SearchAndLoadBlueprint_ReadsManufacturingDataFromSdeYaml()
    {
        var provider = new SdeIndustryDataProvider(tempDirectory);

        var results = await provider.SearchAsync("test product", CancellationToken.None);
        var blueprint = await provider.GetBlueprintAsync(results.Single().BlueprintId, CancellationToken.None);

        Assert.NotNull(blueprint);
        Assert.Equal("Test Blueprint", blueprint.BlueprintName);
        Assert.Equal("Test Product", blueprint.ProductName);
        Assert.Equal(TimeSpan.FromSeconds(60), blueprint.BaseProductionTime);
        Assert.Contains(blueprint.Materials, material => material.Name == "Tritanium" && material.Quantity == 10);
    }

    [Fact]
    public async Task GetPriceAsync_UsesSdeBasePriceAsTemporaryPrice()
    {
        var provider = new SdeIndustryDataProvider(tempDirectory);

        var price = await provider.GetPriceAsync(new TypeId(34), PriceProfile.Empty, CancellationToken.None);

        Assert.NotNull(price);
        Assert.Equal(5m, price.BuyPrice);
        Assert.Equal(5m, price.SellPrice);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
