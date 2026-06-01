namespace EveIndustryPlanner.Core;

public sealed class CompositeMarketPriceProvider(params IMarketPriceProvider[] providers) : IMarketPriceProvider, IMarketOrderBookProvider
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

    public async Task<EffectiveMarketPrice?> GetEffectivePriceAsync(TypeId typeId, PriceProfile profile, MarketPriceSelection selection, long quantity, CancellationToken cancellationToken)
    {
        foreach (var provider in providers)
        {
            if (provider is not IMarketOrderBookProvider orderBookProvider)
            {
                continue;
            }

            var price = await orderBookProvider.GetEffectivePriceAsync(typeId, profile, selection, quantity, cancellationToken);
            if (price is not null)
            {
                return price;
            }
        }

        return null;
    }
}
