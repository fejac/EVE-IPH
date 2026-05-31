namespace EveIndustryPlanner.Core;

public sealed class CompositeMarketPriceProvider(params IMarketPriceProvider[] providers) : IMarketPriceProvider
{
    public async Task<MarketPrice?> GetPriceAsync(TypeId typeId, PriceProfile profile, CancellationToken cancellationToken)
    {
        foreach (var provider in providers)
        {
            var price = await provider.GetPriceAsync(typeId, profile, cancellationToken);
            if (price is not null)
            {
                return price;
            }
        }

        return null;
    }
}
