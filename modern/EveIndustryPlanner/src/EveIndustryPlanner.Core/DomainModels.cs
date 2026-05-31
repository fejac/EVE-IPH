namespace EveIndustryPlanner.Core;

public readonly record struct BlueprintId(long Value);

public readonly record struct TypeId(long Value);

public enum MaterialCategory
{
    Raw,
    Component,
    Optional
}

public enum BlueprintActivityType
{
    Manufacturing,
    Reaction
}

public enum BuildBuyDepth
{
    DirectMaterialsOnly,
    BuildManufacturingComponents,
    BuildManufacturingAndReactions
}

public enum MarketPriceSelection
{
    InstantBuy,
    BuyOrder,
    InstantSell,
    SellOrder,
    BuyMedian,
    SellMedian,
    BuyPercentile,
    SellPercentile,
    BuyWeightedAverage,
    SellWeightedAverage
}

public sealed record BlueprintSearchResult(
    BlueprintId BlueprintId,
    string BlueprintName,
    string ProductName,
    int TechLevel);

public sealed record SolarSystemSearchResult(
    long SolarSystemId,
    string Name,
    double SecurityStatus,
    long RegionId);

public sealed record IndustryCostIndex(
    long SolarSystemId,
    FacilityServiceRole ServiceRole,
    decimal CostIndex,
    DateTimeOffset AsOf);

public sealed class BlueprintDefinition
{
    public BlueprintId BlueprintId { get; init; }
    public TypeId ProductTypeId { get; init; }
    public int ProductGroupId { get; init; }
    public int ProductCategoryId { get; init; }
    public string BlueprintName { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public int TechLevel { get; init; }
    public int ProductQuantity { get; init; } = 1;
    public TimeSpan BaseProductionTime { get; init; }
    public BlueprintActivityType ActivityType { get; init; } = BlueprintActivityType.Manufacturing;
    public IReadOnlyList<BlueprintMaterial> Materials { get; init; } = [];
}

public sealed class BlueprintMaterial
{
    public TypeId TypeId { get; init; }
    public int GroupId { get; init; }
    public int CategoryId { get; init; }
    public string Name { get; init; } = string.Empty;
    public long Quantity { get; init; }
    public double Volume { get; init; }
    public MaterialCategory Category { get; init; }
}

public sealed class MaterialRequirement
{
    public TypeId TypeId { get; init; }
    public string Name { get; init; } = string.Empty;
    public long Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal TotalPrice => UnitPrice * Quantity;
    public double TotalVolume { get; init; }
    public MaterialCategory Category { get; init; }
    public bool MissingPrice { get; init; }
}

public sealed class MarketPrice
{
    public TypeId TypeId { get; init; }
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
    public DateTimeOffset AsOf { get; init; } = DateTimeOffset.UtcNow;

    public decimal Select(MarketPriceSelection selection)
    {
        return selection switch
        {
            MarketPriceSelection.InstantBuy => FirstPositive(SellMinPrice, SellPrice),
            MarketPriceSelection.BuyOrder => FirstPositive(BuyMaxPrice, BuyPrice),
            MarketPriceSelection.InstantSell => FirstPositive(BuyMaxPrice, BuyPrice),
            MarketPriceSelection.SellOrder => FirstPositive(SellMinPrice, SellPrice),
            MarketPriceSelection.BuyMedian => FirstPositive(BuyMedianPrice, BuyPrice),
            MarketPriceSelection.SellMedian => FirstPositive(SellMedianPrice, SellPrice),
            MarketPriceSelection.BuyPercentile => FirstPositive(BuyPercentilePrice, BuyPrice),
            MarketPriceSelection.SellPercentile => FirstPositive(SellPercentilePrice, SellPrice),
            MarketPriceSelection.BuyWeightedAverage => FirstPositive(BuyWeightedAveragePrice, BuyPrice),
            MarketPriceSelection.SellWeightedAverage => FirstPositive(SellWeightedAveragePrice, SellPrice),
            _ => 0
        };
    }

    private static decimal FirstPositive(decimal preferred, decimal fallback)
    {
        return preferred > 0 ? preferred : fallback;
    }
}

public sealed class PriceProfile
{
    public const long DefaultMarketLocationId = 10000002;

    public static PriceProfile Empty { get; } = new();

    public string Name { get; init; } = "Sample Prices";
    public bool UseSellPriceForMaterials { get; init; } = true;
    public bool UseBuyPriceForProducts { get; init; } = true;
    public MarketPriceSelection MaterialPriceSelection { get; init; } = MarketPriceSelection.InstantBuy;
    public MarketPriceSelection ProductPriceSelection { get; init; } = MarketPriceSelection.InstantSell;
    public long MaterialMarketLocationId { get; init; } = DefaultMarketLocationId;
    public string MaterialMarketLocationName { get; init; } = "The Forge";
    public long ProductMarketLocationId { get; init; } = DefaultMarketLocationId;
    public string ProductMarketLocationName { get; init; } = "The Forge";
    public long MarketLocationId { get; init; } = DefaultMarketLocationId;
    public string MarketLocationName { get; init; } = "The Forge";

    public PriceProfile ForMaterialMarket()
    {
        return WithMarketLocation(MaterialMarketLocationId, MaterialMarketLocationName);
    }

    public PriceProfile ForProductMarket()
    {
        return WithMarketLocation(ProductMarketLocationId, ProductMarketLocationName);
    }

    private PriceProfile WithMarketLocation(long marketLocationId, string marketLocationName)
    {
        return new PriceProfile
        {
            Name = Name,
            UseSellPriceForMaterials = UseSellPriceForMaterials,
            UseBuyPriceForProducts = UseBuyPriceForProducts,
            MaterialPriceSelection = MaterialPriceSelection,
            ProductPriceSelection = ProductPriceSelection,
            MaterialMarketLocationId = MaterialMarketLocationId,
            MaterialMarketLocationName = MaterialMarketLocationName,
            ProductMarketLocationId = ProductMarketLocationId,
            ProductMarketLocationName = ProductMarketLocationName,
            MarketLocationId = marketLocationId,
            MarketLocationName = marketLocationName
        };
    }
}

public sealed class FacilityProfile
{
    public static FacilityProfile None { get; } = new()
    {
        Name = "No Facility",
        MaterialMultiplier = 1m,
        TimeMultiplier = 1m,
        JobCostMultiplier = 1m,
        SystemCostIndex = 0.04m
    };

    public string Name { get; init; } = string.Empty;
    public long SolarSystemId { get; init; }
    public string SolarSystemName { get; init; } = string.Empty;
    public string StructureId { get; init; } = "station";
    public string StructureName { get; init; } = "NPC Station / Neutral";
    public FacilityServiceRole ServiceRole { get; init; } = FacilityServiceRole.Manufacturing;
    public IReadOnlyList<string> ServiceModuleIds { get; init; } = [];
    public FacilitySecurityBand SecurityBand { get; init; } = FacilitySecurityBand.HighSec;
    public IReadOnlyList<string> RigIds { get; init; } = [];
    public decimal MaterialMultiplier { get; init; } = 1m;
    public decimal TimeMultiplier { get; init; } = 1m;
    public decimal JobCostMultiplier { get; init; } = 1m;
    public decimal SystemCostIndex { get; init; } = 0.04m;
    public decimal FacilityTaxRate { get; init; }
}

public sealed class ManufacturingRequest
{
    public BlueprintId BlueprintId { get; init; }
    public int Runs { get; init; }
    public int MaterialEfficiency { get; init; }
    public int TimeEfficiency { get; init; }
    public decimal AdditionalCosts { get; init; }
    public bool EnableBuildBuy { get; init; }
    public BuildBuyDepth BuildBuyDepth { get; init; } = BuildBuyDepth.DirectMaterialsOnly;
    public int MaxBuildBuyDepth { get; init; } = 6;
    public FacilityProfile Facility { get; init; } = FacilityProfile.None;
    public FacilityProfile FinalProductFacility { get; init; } = FacilityProfile.None;
    public FacilityProfile ComponentFacility { get; init; } = FacilityProfile.None;
    public FacilityProfile ReactionFacility { get; init; } = FacilityProfile.None;
    public PriceProfile PriceProfile { get; init; } = PriceProfile.Empty;
}

public sealed class BuildBuyDecision
{
    public TypeId TypeId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string FacilityName { get; init; } = string.Empty;
    public long RequiredQuantity { get; init; }
    public bool Built { get; init; }
    public decimal BuyCost { get; init; }
    public decimal BuildCost { get; init; }
    public BlueprintActivityType ActivityType { get; init; }
    public int Depth { get; init; }
}

public sealed class ProductionJobRequirement
{
    public BlueprintId BlueprintId { get; init; }
    public string BlueprintName { get; init; } = string.Empty;
    public TypeId ProductTypeId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public BlueprintActivityType ActivityType { get; init; }
    public string FacilityName { get; init; } = string.Empty;
    public string ParentProductName { get; init; } = string.Empty;
    public long RequiredQuantity { get; init; }
    public int OutputQuantityPerRun { get; init; }
    public int TotalRuns { get; init; }
    public TimeSpan TimePerRun { get; init; }
    public TimeSpan TotalTime { get; init; }
    public int Depth { get; init; }
}

public sealed class ManufacturingResult
{
    public BlueprintDefinition Blueprint { get; init; } = new();
    public IReadOnlyList<MaterialRequirement> Materials { get; init; } = [];
    public IReadOnlyList<BuildBuyDecision> BuildBuyDecisions { get; init; } = [];
    public IReadOnlyList<ProductionJobRequirement> ProductionJobs { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public int OutputQuantity { get; init; }
    public decimal MaterialCost { get; init; }
    public decimal JobCost { get; init; }
    public decimal FinalJobCost { get; init; }
    public decimal BuildJobCost { get; init; }
    public decimal AdditionalCosts { get; init; }
    public decimal TotalCost => MaterialCost + JobCost + AdditionalCosts;
    public decimal EstimatedRevenue { get; init; }
    public decimal Profit => EstimatedRevenue - TotalCost;
    public decimal ProfitPercent => TotalCost == 0 ? 0 : Profit / TotalCost;
    public decimal IskPerHour { get; init; }
    public TimeSpan TotalProductionTime { get; init; }
    public TimeSpan FinalProductionTime { get; init; }
    public TimeSpan BuildProductionTime { get; init; }
    public decimal FinalFacilityMaterialMultiplier { get; init; } = 1m;
    public decimal FinalFacilityTimeMultiplier { get; init; } = 1m;
    public decimal FinalFacilityJobCostMultiplier { get; init; } = 1m;
    public decimal FinalFacilitySystemCostIndex { get; init; }
    public decimal FinalFacilityTaxRate { get; init; }
}

public sealed class ShoppingListLine
{
    public TypeId TypeId { get; init; }
    public string Name { get; init; } = string.Empty;
    public long Quantity { get; init; }
    public decimal EstimatedCost { get; init; }
}

public sealed class ShoppingList
{
    public IReadOnlyList<ShoppingListLine> Lines { get; init; } = [];
    public decimal EstimatedTotal => Lines.Sum(line => line.EstimatedCost);

    public string ToMultibuyText()
    {
        return string.Join(Environment.NewLine, Lines.Select(line => $"{line.Name}\t{line.Quantity}"));
    }
}
