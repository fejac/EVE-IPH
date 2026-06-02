namespace EveIndustryPlanner.Core;

public sealed class SampleBlueprintRepository : IBlueprintRepository, IBlueprintCatalogProvider
{
    private static readonly IReadOnlyList<BlueprintDefinition> Blueprints =
    [
        new BlueprintDefinition
        {
            BlueprintId = new BlueprintId(681),
            ProductTypeId = new TypeId(603),
            BlueprintName = "Merlin Blueprint",
            ProductName = "Merlin",
            TechLevel = 1,
            ProductQuantity = 1,
            BaseProductionTime = TimeSpan.FromMinutes(220),
            Materials =
            [
                new BlueprintMaterial { TypeId = new TypeId(34), Name = "Tritanium", Quantity = 30123, Volume = 0.01, Category = MaterialCategory.Raw },
                new BlueprintMaterial { TypeId = new TypeId(35), Name = "Pyerite", Quantity = 7588, Volume = 0.01, Category = MaterialCategory.Raw },
                new BlueprintMaterial { TypeId = new TypeId(36), Name = "Mexallon", Quantity = 2011, Volume = 0.01, Category = MaterialCategory.Raw },
                new BlueprintMaterial { TypeId = new TypeId(37), Name = "Isogen", Quantity = 523, Volume = 0.01, Category = MaterialCategory.Raw },
                new BlueprintMaterial { TypeId = new TypeId(38), Name = "Nocxium", Quantity = 151, Volume = 0.01, Category = MaterialCategory.Raw }
            ]
        },
        new BlueprintDefinition
        {
            BlueprintId = new BlueprintId(785),
            ProductTypeId = new TypeId(587),
            BlueprintName = "Rifter Blueprint",
            ProductName = "Rifter",
            TechLevel = 1,
            ProductQuantity = 1,
            BaseProductionTime = TimeSpan.FromMinutes(190),
            Materials =
            [
                new BlueprintMaterial { TypeId = new TypeId(34), Name = "Tritanium", Quantity = 25412, Volume = 0.01, Category = MaterialCategory.Raw },
                new BlueprintMaterial { TypeId = new TypeId(35), Name = "Pyerite", Quantity = 6230, Volume = 0.01, Category = MaterialCategory.Raw },
                new BlueprintMaterial { TypeId = new TypeId(36), Name = "Mexallon", Quantity = 1845, Volume = 0.01, Category = MaterialCategory.Raw },
                new BlueprintMaterial { TypeId = new TypeId(37), Name = "Isogen", Quantity = 442, Volume = 0.01, Category = MaterialCategory.Raw }
            ]
        }
    ];

    public Task<IReadOnlyList<BlueprintSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var normalized = query.Trim();
        var results = Blueprints
            .Where(bp => normalized.Length == 0
                         || bp.BlueprintName.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                         || bp.ProductName.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .Select(bp => new BlueprintSearchResult(bp.BlueprintId, bp.BlueprintName, bp.ProductName, bp.TechLevel))
            .ToList();

        return Task.FromResult<IReadOnlyList<BlueprintSearchResult>>(results);
    }

    public Task<IReadOnlyList<BlueprintCatalogItem>> GetManufacturableBlueprintsAsync(CancellationToken cancellationToken)
    {
        var results = Blueprints
            .Select(bp => new BlueprintCatalogItem(
                bp.BlueprintId,
                bp.ProductTypeId,
                bp.ProductGroupId,
                bp.ProductCategoryId,
                bp.BlueprintName,
                bp.ProductName,
                bp.TechLevel,
                bp.MetaGroupId,
                bp.MetaGroupName,
                bp.ActivityType))
            .ToList();

        return Task.FromResult<IReadOnlyList<BlueprintCatalogItem>>(results);
    }

    public Task<BlueprintDefinition?> GetBlueprintAsync(BlueprintId blueprintId, CancellationToken cancellationToken)
    {
        return Task.FromResult(Blueprints.FirstOrDefault(bp => bp.BlueprintId == blueprintId));
    }

    public Task<BlueprintDefinition?> GetBlueprintByProductTypeAsync(TypeId productTypeId, CancellationToken cancellationToken)
    {
        return Task.FromResult(Blueprints.FirstOrDefault(bp => bp.ProductTypeId == productTypeId));
    }
}

public sealed class SampleMarketPriceProvider : IMarketPriceProvider
{
    private static readonly IReadOnlyDictionary<TypeId, MarketPrice> Prices = new Dictionary<TypeId, MarketPrice>
    {
        [new TypeId(34)] = CreatePrice(34, 5.40m, 5.90m),
        [new TypeId(35)] = CreatePrice(35, 11.20m, 12.10m),
        [new TypeId(36)] = CreatePrice(36, 68.00m, 72.00m),
        [new TypeId(37)] = CreatePrice(37, 460.00m, 495.00m),
        [new TypeId(38)] = CreatePrice(38, 980.00m, 1040.00m),
        [new TypeId(587)] = CreatePrice(587, 480000.00m, 520000.00m),
        [new TypeId(603)] = CreatePrice(603, 505000.00m, 550000.00m)
    };

    public Task<MarketPrice?> GetPriceAsync(TypeId typeId, PriceProfile profile, CancellationToken cancellationToken)
    {
        Prices.TryGetValue(typeId, out var price);
        return Task.FromResult(price);
    }

    private static MarketPrice CreatePrice(long typeId, decimal buyPrice, decimal sellPrice)
    {
        return new MarketPrice
        {
            TypeId = new TypeId(typeId),
            BuyPrice = buyPrice,
            SellPrice = sellPrice,
            BuyWeightedAveragePrice = buyPrice,
            BuyMaxPrice = buyPrice,
            BuyMinPrice = buyPrice,
            BuyMedianPrice = buyPrice,
            BuyPercentilePrice = buyPrice,
            SellWeightedAveragePrice = sellPrice,
            SellMaxPrice = sellPrice,
            SellMinPrice = sellPrice,
            SellMedianPrice = sellPrice,
            SellPercentilePrice = sellPrice
        };
    }
}

public sealed class SampleSolarSystemRepository : ISolarSystemRepository
{
    private static readonly IReadOnlyList<SolarSystemSearchResult> Systems =
    [
        new(30000142, "Jita", 0.945913, 10000002),
        new(30002187, "Amarr", 1.0, 10000043),
        new(30002659, "Dodixie", 0.857141, 10000032),
        new(30002510, "Rens", 0.895, 10000030),
        new(30002053, "Hek", 0.468, 10000042)
    ];

    public Task<IReadOnlyList<SolarSystemSearchResult>> SearchSolarSystemsAsync(string query, CancellationToken cancellationToken)
    {
        var normalized = query.Trim();
        var results = Systems
            .Where(system => normalized.Length == 0 || system.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .OrderBy(system => system.Name)
            .ToList();

        return Task.FromResult<IReadOnlyList<SolarSystemSearchResult>>(results);
    }
}

public sealed class SampleIndustryCostIndexProvider : IIndustryCostIndexProvider
{
    public Task<IndustryCostIndex?> GetCostIndexAsync(long solarSystemId, FacilityServiceRole serviceRole, CancellationToken cancellationToken)
    {
        var costIndex = serviceRole == FacilityServiceRole.Reactions ? 0.03m : 0.04m;
        return Task.FromResult<IndustryCostIndex?>(new IndustryCostIndex(solarSystemId, serviceRole, costIndex, DateTimeOffset.UtcNow));
    }
}
