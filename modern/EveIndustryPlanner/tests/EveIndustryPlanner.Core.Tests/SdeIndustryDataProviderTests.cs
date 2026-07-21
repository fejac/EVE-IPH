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
            101:
              activities:
                reaction:
                  materials:
                  - quantity: 100
                    typeID: 34
                  products:
                  - quantity: 200
                    typeID: 201
                  time: 3600
              blueprintTypeID: 101
              maxProductionLimit: 1000
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
              groupID: 25
              metaGroupID: 2
              name:
                en: Test Product
              portionSize: 1
              published: true
              volume: 5.0
            101:
              basePrice: 1000.0
              name:
                en: Test Reaction Formula
              portionSize: 1
              published: true
              volume: 0.01
            201:
              basePrice: 100.0
              name:
                en: Test Reaction Product
              portionSize: 1
              published: true
              volume: 0.1
            """);
        File.WriteAllText(Path.Combine(tempDirectory, "groups.yaml"), """
            25:
              categoryID: 6
            """);
        File.WriteAllText(Path.Combine(tempDirectory, "metaGroups.yaml"), """
            2:
              name:
                en: Tech II
            """);
        File.WriteAllText(Path.Combine(tempDirectory, "typeDogma.yaml"), """
            200:
              dogmaAttributes:
              - attributeID: 422
                value: 2.0
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
    public async Task GetManufacturableBlueprints_ReadsProductTechAndMetaGroup()
    {
        var provider = new SdeIndustryDataProvider(tempDirectory);

        var catalog = await provider.GetManufacturableBlueprintsAsync(CancellationToken.None);
        var item = Assert.Single(catalog, item => item.BlueprintId == new BlueprintId(100));

        Assert.Equal(2, item.TechLevel);
        Assert.Equal(2, item.MetaGroupId);
        Assert.Equal("Tech II", item.MetaGroupName);
        Assert.Equal(6, item.ProductCategoryId);
    }

    [Fact]
    public async Task ReactionFromSde_IgnoresManufacturingMeAndTe()
    {
        var provider = new SdeIndustryDataProvider(tempDirectory);
        var calculator = new ManufacturingCalculator(provider, provider);
        var searchResult = Assert.Single(await provider.SearchAsync("reaction product", CancellationToken.None));

        var result = await calculator.CalculateAsync(new ManufacturingRequest
        {
            BlueprintId = searchResult.BlueprintId,
            Runs = 1,
            MaterialEfficiency = 10,
            TimeEfficiency = 20,
            FinalProductFacility = FacilityProfile.None
        }, CancellationToken.None);

        Assert.Equal(BlueprintActivityType.Reaction, searchResult.ActivityType);
        Assert.Equal(100, Assert.Single(result.Materials).Quantity);
        Assert.Equal(0, Assert.Single(result.ProductionJobs).MaterialEfficiency);
        Assert.Equal(TimeSpan.FromHours(1), result.FinalProductionTime);
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
