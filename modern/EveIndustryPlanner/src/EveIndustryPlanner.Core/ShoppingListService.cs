namespace EveIndustryPlanner.Core;

public sealed class ShoppingListService : IShoppingListService
{
    public ShoppingList CreateFromManufacturingResult(ManufacturingResult result)
    {
        return CreateFromManufacturingResults([result]);
    }

    public ShoppingList CreateFromManufacturingResults(IEnumerable<ManufacturingResult> results)
    {
        var lines = results
            .SelectMany(result => result.Materials)
            .GroupBy(material => new { material.TypeId, material.Name })
            .Select(group => new ShoppingListLine
            {
                TypeId = group.Key.TypeId,
                Name = group.Key.Name,
                Quantity = group.Sum(material => material.Quantity),
                EstimatedCost = group.Sum(material => material.TotalPrice)
            })
            .OrderBy(line => line.Name)
            .ToList();

        return new ShoppingList { Lines = lines };
    }
}
