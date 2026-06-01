using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace EveIndustryPlanner.Core;

public sealed class SdeIndustryDataProvider(string sdeDirectory) : IBlueprintRepository, IBlueprintCatalogProvider, IMarketPriceProvider, ISolarSystemRepository
{
    private readonly Lazy<Task<SdeData>> data = new(() => LoadAsync(sdeDirectory));

    public static bool IsAvailable(string sdeDirectory)
    {
        return File.Exists(Path.Combine(sdeDirectory, "blueprints.yaml"))
            && File.Exists(Path.Combine(sdeDirectory, "types.yaml"));
    }

    public async Task<IReadOnlyList<BlueprintSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var sde = await data.Value.WaitAsync(cancellationToken);
        var normalizedQuery = query.Trim();

        var results = sde.Blueprints.Values
            .Where(blueprint => blueprint.ManufacturingProduct is not null)
            .Select(blueprint => ToSearchResult(sde, blueprint))
            .Where(result => normalizedQuery.Length == 0
                || result.BlueprintName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || result.ProductName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .OrderBy(result => result.ProductName)
            .Take(200)
            .ToList();

        return results;
    }

    public async Task<IReadOnlyList<SolarSystemSearchResult>> SearchSolarSystemsAsync(string query, CancellationToken cancellationToken)
    {
        var sde = await data.Value.WaitAsync(cancellationToken);
        var normalizedQuery = query.Trim();

        var results = sde.SolarSystems.Values
            .Where(system => normalizedQuery.Length == 0
                || system.Name.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .OrderBy(system => system.Name.Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(system => system.Name)
            .Take(100)
            .Select(system => new SolarSystemSearchResult(
                system.SolarSystemId,
                system.Name,
                system.SecurityStatus,
                system.RegionId))
            .ToList();

        return results;
    }

    public async Task<IReadOnlyList<BlueprintCatalogItem>> GetManufacturableBlueprintsAsync(CancellationToken cancellationToken)
    {
        var sde = await data.Value.WaitAsync(cancellationToken);

        return sde.Blueprints.Values
            .Where(blueprint => blueprint.ManufacturingProduct is not null)
            .Select(blueprint =>
            {
                var productTypeId = blueprint.Product?.TypeId ?? 0;
                var productType = GetTypeOrUnknown(sde, productTypeId);
                return new BlueprintCatalogItem(
                    new BlueprintId(blueprint.BlueprintTypeId),
                    new TypeId(productTypeId),
                    productType.GroupId,
                    productType.CategoryId,
                    GetTypeOrUnknown(sde, blueprint.BlueprintTypeId).Name,
                    productType.Name,
                    1,
                    blueprint.ActivityType);
            })
            .OrderBy(item => item.ProductName)
            .ToList();
    }

    public async Task<BlueprintDefinition?> GetBlueprintAsync(BlueprintId blueprintId, CancellationToken cancellationToken)
    {
        var sde = await data.Value.WaitAsync(cancellationToken);
        if (!sde.Blueprints.TryGetValue(blueprintId.Value, out var blueprint) || blueprint.ManufacturingProduct is null)
        {
            return null;
        }

        var productType = GetTypeOrUnknown(sde, blueprint.ManufacturingProduct.TypeId);
        var materials = blueprint.ManufacturingMaterials
            .Select(material =>
            {
                var materialType = GetTypeOrUnknown(sde, material.TypeId);
                return new BlueprintMaterial
                {
                    TypeId = new TypeId(material.TypeId),
                    GroupId = materialType.GroupId,
                    CategoryId = materialType.CategoryId,
                    Name = materialType.Name,
                    Quantity = material.Quantity,
                    Volume = materialType.Volume,
                    Category = sde.ManufacturedTypeIds.Contains(material.TypeId) ? MaterialCategory.Component : MaterialCategory.Raw
                };
            })
            .OrderBy(material => material.Name)
            .ToList();

        return new BlueprintDefinition
        {
            BlueprintId = new BlueprintId(blueprint.BlueprintTypeId),
            ProductTypeId = new TypeId(blueprint.Product!.TypeId),
            ProductGroupId = productType.GroupId,
            ProductCategoryId = productType.CategoryId,
            BlueprintName = GetTypeOrUnknown(sde, blueprint.BlueprintTypeId).Name,
            ProductName = productType.Name,
            TechLevel = 1,
            ProductQuantity = blueprint.Product.Quantity,
            BaseProductionTime = TimeSpan.FromSeconds(blueprint.Activity.Time),
            ActivityType = blueprint.ActivityType,
            Materials = materials
        };
    }

    public async Task<BlueprintDefinition?> GetBlueprintByProductTypeAsync(TypeId productTypeId, CancellationToken cancellationToken)
    {
        var sde = await data.Value.WaitAsync(cancellationToken);
        if (!sde.BlueprintsByProductTypeId.TryGetValue(productTypeId.Value, out var blueprintId))
        {
            return null;
        }

        return await GetBlueprintAsync(new BlueprintId(blueprintId), cancellationToken);
    }

    public async Task<MarketPrice?> GetPriceAsync(TypeId typeId, PriceProfile profile, CancellationToken cancellationToken)
    {
        var sde = await data.Value.WaitAsync(cancellationToken);
        if (!sde.Types.TryGetValue(typeId.Value, out var type) || type.BasePrice <= 0)
        {
            return null;
        }

        return new MarketPrice
        {
            TypeId = typeId,
            BuyPrice = type.BasePrice,
            SellPrice = type.BasePrice,
            BuyWeightedAveragePrice = type.BasePrice,
            BuyMaxPrice = type.BasePrice,
            BuyMinPrice = type.BasePrice,
            BuyMedianPrice = type.BasePrice,
            BuyPercentilePrice = type.BasePrice,
            SellWeightedAveragePrice = type.BasePrice,
            SellMaxPrice = type.BasePrice,
            SellMinPrice = type.BasePrice,
            SellMedianPrice = type.BasePrice,
            SellPercentilePrice = type.BasePrice,
            AsOf = sde.LoadedAt
        };
    }

    private static BlueprintSearchResult ToSearchResult(SdeData sde, SdeBlueprint blueprint)
    {
        var productTypeId = blueprint.Product?.TypeId ?? 0;
        return new BlueprintSearchResult(
            new BlueprintId(blueprint.BlueprintTypeId),
            GetTypeOrUnknown(sde, blueprint.BlueprintTypeId).Name,
            GetTypeOrUnknown(sde, productTypeId).Name,
            1);
    }

    private static SdeTypeInfo GetTypeOrUnknown(SdeData sde, long typeId)
    {
        return sde.Types.TryGetValue(typeId, out var type)
            ? type
            : new SdeTypeInfo { Name = $"Type {typeId}" };
    }

    private static async Task<SdeData> LoadAsync(string sdeDirectory)
    {
        var signature = BuildSdeSignature(sdeDirectory);
        var cacheFilePath = GetCacheFilePath(sdeDirectory);
        var cached = await TryLoadFromCacheAsync(cacheFilePath, signature);
        if (cached is not null)
        {
            return cached;
        }

        var deserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();

        await using var blueprintStream = File.OpenRead(Path.Combine(sdeDirectory, "blueprints.yaml"));
        using var blueprintReader = new StreamReader(blueprintStream);
        var blueprints = deserializer.Deserialize<Dictionary<long, SdeBlueprint>>(blueprintReader);

        await using var typeStream = File.OpenRead(Path.Combine(sdeDirectory, "types.yaml"));
        using var typeReader = new StreamReader(typeStream);
        var groupPath = Path.Combine(sdeDirectory, "groups.yaml");
        var groups = File.Exists(groupPath)
            ? deserializer.Deserialize<Dictionary<int, SdeGroup>>(File.ReadAllText(groupPath))
            : [];

        var types = deserializer.Deserialize<Dictionary<long, SdeType>>(typeReader)
            .ToDictionary(
                pair => pair.Key,
                pair => new SdeTypeInfo
                {
                    Name = pair.Value.Name.TryGetValue("en", out var englishName) ? englishName : $"Type {pair.Key}",
                    GroupId = pair.Value.GroupId,
                    CategoryId = groups.TryGetValue(pair.Value.GroupId, out var group) ? group.CategoryId : 0,
                    BasePrice = pair.Value.BasePrice,
                    Volume = pair.Value.Volume,
                    PortionSize = pair.Value.PortionSize,
                    Published = pair.Value.Published
                });

        var manufacturedTypeIds = blueprints.Values
            .Select(blueprint => blueprint.Product?.TypeId)
            .Where(typeId => typeId.HasValue)
            .Select(typeId => typeId!.Value)
            .ToHashSet();

        var blueprintsByProductTypeId = blueprints.Values
            .Where(blueprint => blueprint.Product is not null)
            .GroupBy(blueprint => blueprint.Product!.TypeId)
            .ToDictionary(group => group.Key, group => group.First().BlueprintTypeId);

        var solarSystems = new Dictionary<long, SdeSolarSystemInfo>();
        var solarSystemPath = Path.Combine(sdeDirectory, "mapSolarSystems.yaml");
        if (File.Exists(solarSystemPath))
        {
            await using var solarSystemStream = File.OpenRead(solarSystemPath);
            using var solarSystemReader = new StreamReader(solarSystemStream);
            solarSystems = deserializer.Deserialize<Dictionary<long, SdeSolarSystem>>(solarSystemReader)
                .ToDictionary(
                    pair => pair.Key,
                    pair => new SdeSolarSystemInfo
                    {
                        SolarSystemId = pair.Key,
                        Name = pair.Value.Name.TryGetValue("en", out var englishName) ? englishName : $"System {pair.Key}",
                        SecurityStatus = pair.Value.SecurityStatus,
                        RegionId = pair.Value.RegionId
                    });
        }

        var loadedData = new SdeData(blueprints, blueprintsByProductTypeId, types, manufacturedTypeIds, solarSystems, DateTimeOffset.UtcNow);
        await TrySaveToCacheAsync(cacheFilePath, signature, loadedData);
        return loadedData;
    }

    private static async Task<SdeData?> TryLoadFromCacheAsync(string cacheFilePath, string signature)
    {
        try
        {
            if (!File.Exists(cacheFilePath))
            {
                return null;
            }

            await using var fileStream = File.OpenRead(cacheFilePath);
            await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            var document = await JsonSerializer.DeserializeAsync<SdeCacheDocument>(gzipStream, CacheJsonOptions);

            if (document is null || document.Signature != signature)
            {
                return null;
            }

            return new SdeData(
                document.Blueprints,
                document.BlueprintsByProductTypeId,
                document.Types,
                document.ManufacturedTypeIds,
                document.SolarSystems,
                document.LoadedAt);
        }
        catch (IOException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task TrySaveToCacheAsync(string cacheFilePath, string signature, SdeData data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cacheFilePath)!);

            var document = new SdeCacheDocument
            {
                Signature = signature,
                Blueprints = data.Blueprints.ToDictionary(),
                BlueprintsByProductTypeId = data.BlueprintsByProductTypeId.ToDictionary(),
                Types = data.Types.ToDictionary(),
                ManufacturedTypeIds = data.ManufacturedTypeIds.ToHashSet(),
                SolarSystems = data.SolarSystems.ToDictionary(),
                LoadedAt = data.LoadedAt
            };

            await using var fileStream = File.Create(cacheFilePath);
            await using var gzipStream = new GZipStream(fileStream, CompressionLevel.Fastest);
            await JsonSerializer.SerializeAsync(gzipStream, document, CacheJsonOptions);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string BuildSdeSignature(string sdeDirectory)
    {
        var files = new[]
        {
            "blueprints.yaml",
            "types.yaml",
            "groups.yaml",
            "mapSolarSystems.yaml"
        };

        return string.Join("|", files.Select(fileName =>
        {
            var path = Path.Combine(sdeDirectory, fileName);
            if (!File.Exists(path))
            {
                return $"{fileName}:missing";
            }

            var info = new FileInfo(path);
            return $"{fileName}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        }));
    }

    private static string GetCacheFilePath(string sdeDirectory)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(sdeDirectory).ToUpperInvariant()));
        var hash = Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
        return Path.Combine(appData, "EveIndustryPlanner", $"sde-cache-{hash}.json.gz");
    }

    private static readonly JsonSerializerOptions CacheJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record SdeData(
        IReadOnlyDictionary<long, SdeBlueprint> Blueprints,
        IReadOnlyDictionary<long, long> BlueprintsByProductTypeId,
        IReadOnlyDictionary<long, SdeTypeInfo> Types,
        IReadOnlySet<long> ManufacturedTypeIds,
        IReadOnlyDictionary<long, SdeSolarSystemInfo> SolarSystems,
        DateTimeOffset LoadedAt);

    private sealed class SdeBlueprint
    {
        [YamlMember(Alias = "blueprintTypeID")]
        public long BlueprintTypeId { get; init; }

        [YamlMember(Alias = "activities")]
        public SdeActivities Activities { get; init; } = new();

        [JsonIgnore]
        public SdeActivity Activity => Activities.Manufacturing.Products.Count > 0
            ? Activities.Manufacturing
            : Activities.Reaction;

        [JsonIgnore]
        public BlueprintActivityType ActivityType => Activities.Manufacturing.Products.Count > 0
            ? BlueprintActivityType.Manufacturing
            : BlueprintActivityType.Reaction;

        [JsonIgnore]
        public SdeProduct? Product => Activity.Products.FirstOrDefault();

        [JsonIgnore]
        public SdeProduct? ManufacturingProduct => Product;

        [JsonIgnore]
        public IReadOnlyList<SdeMaterial> ManufacturingMaterials => Activity.Materials;
    }

    private sealed class SdeActivities
    {
        [YamlMember(Alias = "manufacturing")]
        public SdeActivity Manufacturing { get; init; } = new();

        [YamlMember(Alias = "reaction")]
        public SdeActivity Reaction { get; init; } = new();
    }

    private sealed class SdeActivity
    {
        [YamlMember(Alias = "materials")]
        public List<SdeMaterial> Materials { get; init; } = [];

        [YamlMember(Alias = "products")]
        public List<SdeProduct> Products { get; init; } = [];

        [YamlMember(Alias = "time")]
        public int Time { get; init; }
    }

    private sealed class SdeMaterial
    {
        [YamlMember(Alias = "typeID")]
        public long TypeId { get; init; }

        [YamlMember(Alias = "quantity")]
        public long Quantity { get; init; }
    }

    private sealed class SdeProduct
    {
        [YamlMember(Alias = "typeID")]
        public long TypeId { get; init; }

        [YamlMember(Alias = "quantity")]
        public int Quantity { get; init; } = 1;
    }

    private sealed class SdeType
    {
        [YamlMember(Alias = "name")]
        public Dictionary<string, string> Name { get; init; } = [];

        [YamlMember(Alias = "groupID")]
        public int GroupId { get; init; }

        [YamlMember(Alias = "basePrice")]
        public decimal BasePrice { get; init; }

        [YamlMember(Alias = "portionSize")]
        public int PortionSize { get; init; } = 1;

        [YamlMember(Alias = "published")]
        public bool Published { get; init; }

        [YamlMember(Alias = "volume")]
        public double Volume { get; init; }
    }

    private sealed class SdeGroup
    {
        [YamlMember(Alias = "categoryID")]
        public int CategoryId { get; init; }
    }

    private sealed class SdeTypeInfo
    {
        public string Name { get; init; } = string.Empty;
        public int GroupId { get; init; }
        public int CategoryId { get; init; }
        public decimal BasePrice { get; init; }
        public int PortionSize { get; init; } = 1;
        public bool Published { get; init; }
        public double Volume { get; init; }
    }

    private sealed class SdeSolarSystem
    {
        [YamlMember(Alias = "name")]
        public Dictionary<string, string> Name { get; init; } = [];

        [YamlMember(Alias = "securityStatus")]
        public double SecurityStatus { get; init; }

        [YamlMember(Alias = "regionID")]
        public long RegionId { get; init; }
    }

    private sealed class SdeSolarSystemInfo
    {
        public long SolarSystemId { get; init; }
        public string Name { get; init; } = string.Empty;
        public double SecurityStatus { get; init; }
        public long RegionId { get; init; }
    }

    private sealed class SdeCacheDocument
    {
        public string Signature { get; init; } = string.Empty;
        public Dictionary<long, SdeBlueprint> Blueprints { get; init; } = [];
        public Dictionary<long, long> BlueprintsByProductTypeId { get; init; } = [];
        public Dictionary<long, SdeTypeInfo> Types { get; init; } = [];
        public HashSet<long> ManufacturedTypeIds { get; init; } = [];
        public Dictionary<long, SdeSolarSystemInfo> SolarSystems { get; init; } = [];
        public DateTimeOffset LoadedAt { get; init; }
    }
}
