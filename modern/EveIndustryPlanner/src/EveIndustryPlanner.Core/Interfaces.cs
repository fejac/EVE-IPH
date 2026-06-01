namespace EveIndustryPlanner.Core;

public interface IBlueprintRepository
{
    Task<IReadOnlyList<BlueprintSearchResult>> SearchAsync(string query, CancellationToken cancellationToken);
    Task<BlueprintDefinition?> GetBlueprintAsync(BlueprintId blueprintId, CancellationToken cancellationToken);
    Task<BlueprintDefinition?> GetBlueprintByProductTypeAsync(TypeId productTypeId, CancellationToken cancellationToken);
}

public interface IBlueprintCatalogProvider
{
    Task<IReadOnlyList<BlueprintCatalogItem>> GetManufacturableBlueprintsAsync(CancellationToken cancellationToken);
}

public interface IMarketPriceProvider
{
    Task<MarketPrice?> GetPriceAsync(TypeId typeId, PriceProfile profile, CancellationToken cancellationToken);
}

public interface IMarketOrderBookProvider
{
    Task<EffectiveMarketPrice?> GetEffectivePriceAsync(TypeId typeId, PriceProfile profile, MarketPriceSelection selection, long quantity, CancellationToken cancellationToken);
}

public interface ISolarSystemRepository
{
    Task<IReadOnlyList<SolarSystemSearchResult>> SearchSolarSystemsAsync(string query, CancellationToken cancellationToken);
}

public interface IIndustryCostIndexProvider
{
    Task<IndustryCostIndex?> GetCostIndexAsync(long solarSystemId, FacilityServiceRole serviceRole, CancellationToken cancellationToken);
}

public interface IManufacturingCalculator
{
    Task<ManufacturingResult> CalculateAsync(ManufacturingRequest request, CancellationToken cancellationToken);
}

public interface IShoppingListService
{
    ShoppingList CreateFromManufacturingResult(ManufacturingResult result);
    ShoppingList CreateFromManufacturingResults(IEnumerable<ManufacturingResult> results);
}
