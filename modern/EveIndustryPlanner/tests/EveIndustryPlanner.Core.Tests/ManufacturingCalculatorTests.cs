using EveIndustryPlanner.Core;

namespace EveIndustryPlanner.Core.Tests;

public sealed class ManufacturingCalculatorTests
{
    [Fact]
    public void MaterialEfficiency_ReducesQuantityAndRoundsUp()
    {
        var quantity = ManufacturingCalculator.CalculateMaterialQuantity(100, runs: 3, materialEfficiency: 10, facilityMaterialMultiplier: 1m);

        Assert.Equal(270, quantity);
    }

    [Fact]
    public void FacilityMaterialMultiplier_ReducesQuantityAndRoundsUp()
    {
        var quantity = ManufacturingCalculator.CalculateMaterialQuantity(101, runs: 1, materialEfficiency: 0, facilityMaterialMultiplier: 0.99m);

        Assert.Equal(100, quantity);
    }

    [Fact]
    public async Task CalculateAsync_ReturnsExpectedSampleBlueprintResult()
    {
        var calculator = new ManufacturingCalculator(new SampleBlueprintRepository(), new SampleMarketPriceProvider());

        var result = await calculator.CalculateAsync(new ManufacturingRequest
        {
            BlueprintId = new BlueprintId(681),
            Runs = 2,
            MaterialEfficiency = 10,
            TimeEfficiency = 20,
            Facility = FacilityProfile.None
        }, CancellationToken.None);

        Assert.Equal("Merlin", result.Blueprint.ProductName);
        Assert.Equal(2, result.OutputQuantity);
        Assert.Contains(result.Materials, material => material.Name == "Tritanium" && material.Quantity == 54222);
        Assert.Contains(result.ProductionJobs, job => job.ProductName == "Merlin" && job.TotalRuns == 2 && job.OutputQuantityPerRun == 1);
        Assert.True(result.MaterialCost > 0);
        Assert.True(result.TotalProductionTime < TimeSpan.FromMinutes(440));
    }

    [Fact]
    public async Task CalculateAsync_AddsAdditionalCostsToTotalCostAndProfit()
    {
        var calculator = new ManufacturingCalculator(new SampleBlueprintRepository(), new SampleMarketPriceProvider());
        var request = new ManufacturingRequest
        {
            BlueprintId = new BlueprintId(681),
            Runs = 1,
            MaterialEfficiency = 10,
            TimeEfficiency = 20,
            Facility = FacilityProfile.None
        };

        var baseline = await calculator.CalculateAsync(request, CancellationToken.None);
        var withAdditionalCosts = await calculator.CalculateAsync(new ManufacturingRequest
        {
            BlueprintId = request.BlueprintId,
            Runs = request.Runs,
            MaterialEfficiency = request.MaterialEfficiency,
            TimeEfficiency = request.TimeEfficiency,
            AdditionalCosts = 500_000m,
            Facility = request.Facility,
            PriceProfile = request.PriceProfile
        }, CancellationToken.None);

        Assert.Equal(500_000m, withAdditionalCosts.AdditionalCosts);
        Assert.Equal(baseline.TotalCost + 500_000m, withAdditionalCosts.TotalCost);
        Assert.Equal(baseline.Profit - 500_000m, withAdditionalCosts.Profit);
    }

    [Fact]
    public async Task CalculateAsync_UsesSeparateMarketsForMaterialsAndProduct()
    {
        var priceProvider = new CapturingPriceProvider();
        var calculator = new ManufacturingCalculator(new SampleBlueprintRepository(), priceProvider);

        await calculator.CalculateAsync(new ManufacturingRequest
        {
            BlueprintId = new BlueprintId(681),
            Runs = 1,
            MaterialEfficiency = 10,
            TimeEfficiency = 20,
            Facility = FacilityProfile.None,
            PriceProfile = new PriceProfile
            {
                MaterialMarketLocationId = 30000142,
                ProductMarketLocationId = 60003760
            }
        }, CancellationToken.None);

        Assert.Contains(30000142, priceProvider.LocationIds);
        Assert.Contains(60003760, priceProvider.LocationIds);
    }

    [Fact]
    public async Task CalculateAsync_UsesConfiguredPriceStrategies()
    {
        var calculator = new ManufacturingCalculator(new SampleBlueprintRepository(), new StrategyPriceProvider());

        var result = await calculator.CalculateAsync(new ManufacturingRequest
        {
            BlueprintId = new BlueprintId(681),
            Runs = 1,
            MaterialEfficiency = 0,
            TimeEfficiency = 0,
            Facility = FacilityProfile.None,
            PriceProfile = new PriceProfile
            {
                MaterialPriceSelection = MarketPriceSelection.BuyOrder,
                ProductPriceSelection = MarketPriceSelection.SellOrder
            }
        }, CancellationToken.None);

        Assert.All(result.Materials, material => Assert.Equal(6m, material.UnitPrice));
        Assert.Equal(120m, result.EstimatedRevenue);
    }

    [Fact]
    public async Task CalculateAsync_InstantBuyUsesOrderBookVolumeForMaterials()
    {
        var calculator = new ManufacturingCalculator(new VolumeAwareRepository(), new VolumeAwarePriceProvider());

        var result = await calculator.CalculateAsync(new ManufacturingRequest
        {
            BlueprintId = new BlueprintId(10),
            Runs = 1,
            MaterialEfficiency = 0,
            TimeEfficiency = 0,
            Facility = FacilityProfile.None,
            PriceProfile = new PriceProfile
            {
                MaterialPriceSelection = MarketPriceSelection.InstantBuy,
                ProductPriceSelection = MarketPriceSelection.SellOrder
            }
        }, CancellationToken.None);

        var material = Assert.Single(result.Materials);
        Assert.Equal(190, material.Quantity);
        Assert.Equal(901_000m / 190m, material.UnitPrice);
        Assert.Equal(901_000m, material.TotalPrice);
    }

    [Fact]
    public async Task CalculateAsync_BuildBuyBuildsCheaperManufacturingComponent()
    {
        var calculator = new ManufacturingCalculator(new BuildBuyRepository(BlueprintActivityType.Manufacturing), new BuildBuyPriceProvider());

        var result = await calculator.CalculateAsync(new ManufacturingRequest
        {
            BlueprintId = new BlueprintId(1),
            Runs = 1,
            MaterialEfficiency = 0,
            TimeEfficiency = 0,
            EnableBuildBuy = true,
            BuildBuyDepth = BuildBuyDepth.BuildManufacturingComponents,
            Facility = new FacilityProfile { SystemCostIndex = 0 }
        }, CancellationToken.None);

        Assert.Contains(result.BuildBuyDecisions, decision => decision.Name == "Component" && decision.Built);
        Assert.Contains(result.ProductionJobs, job => job.ProductName == "Component" && job.TotalRuns == 1);
        Assert.DoesNotContain(result.Materials, material => material.Name == "Component");
        Assert.Contains(result.Materials, material => material.Name == "Raw Material");
    }

    [Fact]
    public async Task CalculateAsync_BuildBuyUsesComponentFacilityForComponentMaterials()
    {
        var calculator = new ManufacturingCalculator(new BuildBuyRepository(BlueprintActivityType.Manufacturing), new BuildBuyPriceProvider());

        var result = await calculator.CalculateAsync(new ManufacturingRequest
        {
            BlueprintId = new BlueprintId(1),
            Runs = 1,
            MaterialEfficiency = 0,
            TimeEfficiency = 0,
            EnableBuildBuy = true,
            BuildBuyDepth = BuildBuyDepth.BuildManufacturingComponents,
            FinalProductFacility = new FacilityProfile { SystemCostIndex = 0, MaterialMultiplier = 1m, TimeMultiplier = 1m, JobCostMultiplier = 1m },
            ComponentFacility = new FacilityProfile { SystemCostIndex = 0, MaterialMultiplier = 2m, TimeMultiplier = 1m, JobCostMultiplier = 1m }
        }, CancellationToken.None);

        Assert.Contains(result.Materials, material => material.Name == "Raw Material" && material.Quantity == 2);
    }

    [Fact]
    public async Task CalculateAsync_BuildBuyUsesFinalFacilityForT1BaseItems()
    {
        var calculator = new ManufacturingCalculator(new BuildBuyRepository(BlueprintActivityType.Manufacturing, materialCategoryId: 6), new BuildBuyPriceProvider());

        var result = await calculator.CalculateAsync(new ManufacturingRequest
        {
            BlueprintId = new BlueprintId(1),
            Runs = 1,
            MaterialEfficiency = 0,
            TimeEfficiency = 0,
            EnableBuildBuy = true,
            BuildBuyDepth = BuildBuyDepth.BuildManufacturingComponents,
            FinalProductFacility = new FacilityProfile { Name = "Final", SystemCostIndex = 0, MaterialMultiplier = 3m, TimeMultiplier = 1m, JobCostMultiplier = 1m },
            ComponentFacility = new FacilityProfile { Name = "Components", SystemCostIndex = 0, MaterialMultiplier = 5m, TimeMultiplier = 1m, JobCostMultiplier = 1m }
        }, CancellationToken.None);

        Assert.Contains(result.Materials, material => material.Name == "Raw Material" && material.Quantity == 9);
        Assert.Contains(result.BuildBuyDecisions, decision => decision.Name == "Component" && decision.FacilityName == "Final");
    }

    [Fact]
    public async Task CalculateAsync_BuildBuyDoesNotBuildReactionWhenDepthExcludesReactions()
    {
        var calculator = new ManufacturingCalculator(new BuildBuyRepository(BlueprintActivityType.Reaction), new BuildBuyPriceProvider());

        var result = await calculator.CalculateAsync(new ManufacturingRequest
        {
            BlueprintId = new BlueprintId(1),
            Runs = 1,
            MaterialEfficiency = 0,
            TimeEfficiency = 0,
            EnableBuildBuy = true,
            BuildBuyDepth = BuildBuyDepth.BuildManufacturingComponents,
            Facility = new FacilityProfile { SystemCostIndex = 0 }
        }, CancellationToken.None);

        Assert.Empty(result.BuildBuyDecisions);
        Assert.Contains(result.Materials, material => material.Name == "Component");
    }

    [Fact]
    public void ShoppingList_AggregatesDuplicateMaterials()
    {
        var service = new ShoppingListService();
        var result = new ManufacturingResult
        {
            Materials =
            [
                new MaterialRequirement { TypeId = new TypeId(34), Name = "Tritanium", Quantity = 10, UnitPrice = 5 },
                new MaterialRequirement { TypeId = new TypeId(34), Name = "Tritanium", Quantity = 15, UnitPrice = 5 }
            ]
        };

        var shoppingList = service.CreateFromManufacturingResult(result);

        var line = Assert.Single(shoppingList.Lines);
        Assert.Equal(25, line.Quantity);
        Assert.Equal(125, line.EstimatedCost);
    }

    [Fact]
    public void ShoppingList_AggregatesMultipleManufacturingResults()
    {
        var service = new ShoppingListService();
        var first = new ManufacturingResult
        {
            Materials =
            [
                new MaterialRequirement { TypeId = new TypeId(34), Name = "Tritanium", Quantity = 10, UnitPrice = 5 }
            ]
        };
        var second = new ManufacturingResult
        {
            Materials =
            [
                new MaterialRequirement { TypeId = new TypeId(34), Name = "Tritanium", Quantity = 15, UnitPrice = 6 },
                new MaterialRequirement { TypeId = new TypeId(35), Name = "Pyerite", Quantity = 20, UnitPrice = 3 }
            ]
        };

        var shoppingList = service.CreateFromManufacturingResults([first, second]);

        Assert.Equal(2, shoppingList.Lines.Count);
        Assert.Contains(shoppingList.Lines, line => line.Name == "Tritanium" && line.Quantity == 25 && line.EstimatedCost == 140);
        Assert.Contains(shoppingList.Lines, line => line.Name == "Pyerite" && line.Quantity == 20 && line.EstimatedCost == 60);
    }

    private sealed class CapturingPriceProvider : IMarketPriceProvider
    {
        public List<long> LocationIds { get; } = [];

        public Task<MarketPrice?> GetPriceAsync(TypeId typeId, PriceProfile profile, CancellationToken cancellationToken)
        {
            LocationIds.Add(profile.MarketLocationId);
            return Task.FromResult<MarketPrice?>(new MarketPrice
            {
                TypeId = typeId,
                BuyPrice = 10,
                SellPrice = 12
            });
        }
    }

    private sealed class StrategyPriceProvider : IMarketPriceProvider
    {
        public Task<MarketPrice?> GetPriceAsync(TypeId typeId, PriceProfile profile, CancellationToken cancellationToken)
        {
            var isMerlin = typeId == new TypeId(603);
            return Task.FromResult<MarketPrice?>(new MarketPrice
            {
                TypeId = typeId,
                BuyPrice = isMerlin ? 100 : 6,
                SellPrice = isMerlin ? 120 : 10,
                BuyMaxPrice = isMerlin ? 100 : 6,
                SellMinPrice = isMerlin ? 120 : 10
            });
        }
    }

    private sealed class BuildBuyRepository(BlueprintActivityType componentActivityType, int materialCategoryId = 0) : IBlueprintRepository
    {
        private readonly BlueprintDefinition root = new()
        {
            BlueprintId = new BlueprintId(1),
            ProductTypeId = new TypeId(100),
            ProductName = "Product",
            BlueprintName = "Product Blueprint",
            ProductQuantity = 1,
            BaseProductionTime = TimeSpan.FromMinutes(1),
            Materials =
            [
                new BlueprintMaterial { TypeId = new TypeId(200), Name = "Component", Quantity = 1, Volume = 1, Category = MaterialCategory.Component, CategoryId = materialCategoryId }
            ]
        };

        public Task<IReadOnlyList<BlueprintSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<BlueprintSearchResult>>([]);
        }

        public Task<BlueprintDefinition?> GetBlueprintAsync(BlueprintId blueprintId, CancellationToken cancellationToken)
        {
            return Task.FromResult<BlueprintDefinition?>(blueprintId == root.BlueprintId ? root : CreateComponentBlueprint());
        }

        public Task<BlueprintDefinition?> GetBlueprintByProductTypeAsync(TypeId productTypeId, CancellationToken cancellationToken)
        {
            return Task.FromResult<BlueprintDefinition?>(productTypeId == new TypeId(200) ? CreateComponentBlueprint() : null);
        }

        private BlueprintDefinition CreateComponentBlueprint()
        {
            return new BlueprintDefinition
            {
                BlueprintId = new BlueprintId(2),
                ProductTypeId = new TypeId(200),
                ProductName = "Component",
                BlueprintName = "Component Blueprint",
                ProductQuantity = 1,
                BaseProductionTime = TimeSpan.FromMinutes(1),
                ActivityType = componentActivityType,
                Materials =
                [
                    new BlueprintMaterial { TypeId = new TypeId(300), Name = "Raw Material", Quantity = 1, Volume = 1, Category = MaterialCategory.Raw }
                ]
            };
        }
    }

    private sealed class BuildBuyPriceProvider : IMarketPriceProvider
    {
        public Task<MarketPrice?> GetPriceAsync(TypeId typeId, PriceProfile profile, CancellationToken cancellationToken)
        {
            var price = typeId.Value switch
            {
                100 => 1000m,
                200 => 100m,
                300 => 10m,
                _ => 0m
            };

            return Task.FromResult<MarketPrice?>(new MarketPrice
            {
                TypeId = typeId,
                BuyPrice = price,
                SellPrice = price,
                BuyMaxPrice = price,
                SellMinPrice = price
            });
        }
    }

    private sealed class VolumeAwareRepository : IBlueprintRepository
    {
        private readonly BlueprintDefinition blueprint = new()
        {
            BlueprintId = new BlueprintId(10),
            ProductTypeId = new TypeId(20),
            ProductName = "Product",
            BlueprintName = "Product Blueprint",
            ProductQuantity = 1,
            BaseProductionTime = TimeSpan.FromMinutes(1),
            Materials =
            [
                new BlueprintMaterial { TypeId = new TypeId(30), Name = "Volume Material", Quantity = 190, Volume = 1, Category = MaterialCategory.Raw }
            ]
        };

        public Task<IReadOnlyList<BlueprintSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<BlueprintSearchResult>>([]);
        }

        public Task<BlueprintDefinition?> GetBlueprintAsync(BlueprintId blueprintId, CancellationToken cancellationToken)
        {
            return Task.FromResult<BlueprintDefinition?>(blueprintId == blueprint.BlueprintId ? blueprint : null);
        }

        public Task<BlueprintDefinition?> GetBlueprintByProductTypeAsync(TypeId productTypeId, CancellationToken cancellationToken)
        {
            return Task.FromResult<BlueprintDefinition?>(null);
        }
    }

    private sealed class VolumeAwarePriceProvider : IMarketPriceProvider, IMarketOrderBookProvider
    {
        public Task<MarketPrice?> GetPriceAsync(TypeId typeId, PriceProfile profile, CancellationToken cancellationToken)
        {
            return Task.FromResult<MarketPrice?>(new MarketPrice
            {
                TypeId = typeId,
                SellPrice = 1_000_000,
                SellMinPrice = 1_000_000
            });
        }

        public Task<EffectiveMarketPrice?> GetEffectivePriceAsync(TypeId typeId, PriceProfile profile, MarketPriceSelection selection, long quantity, CancellationToken cancellationToken)
        {
            var totalPrice = 10m * 100m + 180m * 5_000m;
            return Task.FromResult<EffectiveMarketPrice?>(new EffectiveMarketPrice
            {
                UnitPrice = totalPrice / quantity,
                TotalPrice = totalPrice,
                FilledQuantity = quantity,
                RequestedQuantity = quantity
            });
        }
    }
}
