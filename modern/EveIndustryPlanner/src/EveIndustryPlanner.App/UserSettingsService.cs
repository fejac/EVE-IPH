using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using EveIndustryPlanner.Core;

namespace EveIndustryPlanner.App;

public sealed class UserSettingsService(string settingsFilePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(settingsFilePath))
            {
                return new UserSettings();
            }

            using var stream = File.OpenRead(settingsFilePath);
            return JsonSerializer.Deserialize<UserSettings>(stream, JsonOptions) ?? new UserSettings();
        }
        catch (IOException)
        {
            return new UserSettings();
        }
        catch (JsonException)
        {
            return new UserSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new UserSettings();
        }
    }

    public static string GetDefaultSettingsFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "EveIndustryPlanner", "settings.json");
    }

    public void Save(UserSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(settingsFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = File.Create(settingsFilePath);
            JsonSerializer.Serialize(stream, settings, JsonOptions);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public sealed class UserSettings
{
    public const string DefaultPocketBaseUrl = "https://eve.fejac.cz";

    public List<SavedFacilityProfile> FacilityProfiles { get; init; } =
    [
        SavedFacilityProfile.None
    ];

    public List<SavedCharacterAccount> CharacterAccounts { get; init; } = [];

    public Guid FinalProductFacilityId { get; init; } = SavedFacilityProfile.NoneId;
    public Guid ComponentFacilityId { get; init; } = SavedFacilityProfile.NoneId;
    public Guid ReactionFacilityId { get; init; } = SavedFacilityProfile.NoneId;
    public long MaterialMarketLocationId { get; init; } = PriceProfile.DefaultMarketLocationId;
    public long ProductMarketLocationId { get; init; } = PriceProfile.DefaultMarketLocationId;
    public MarketPriceSelection MaterialPriceSelection { get; init; } = MarketPriceSelection.InstantBuy;
    public MarketPriceSelection ProductPriceSelection { get; init; } = MarketPriceSelection.InstantSell;
    public bool EnableBuildBuy { get; init; }
    public BuildBuyDepth BuildBuyDepth { get; init; } = BuildBuyDepth.DirectMaterialsOnly;
    public int MaxBuildBuyDepth { get; init; } = 6;
    public string PocketBaseUrl { get; init; } = DefaultPocketBaseUrl;
}

public sealed class SavedCharacterAccount
{
    public long CharacterId { get; init; }
    public string CharacterName { get; init; } = string.Empty;
    public string PocketBaseAuthToken { get; init; } = string.Empty;
    public List<string> Scopes { get; init; } = [];
    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class SavedFacilityProfile
{
    public static Guid NoneId { get; } = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public static SavedFacilityProfile None { get; } = new()
    {
        Id = NoneId,
        Name = "No Facility",
        SolarSystemName = "Unspecified",
        StructureId = "station",
        StructureName = "NPC Station / Neutral",
        ServiceRole = FacilityServiceRole.Manufacturing,
        ServiceModuleIds = [],
        SecurityBand = FacilitySecurityBand.HighSec,
        RigIds = [],
        MaterialMultiplier = 1m,
        TimeMultiplier = 1m,
        JobCostMultiplier = 1m,
        SystemCostIndex = 0.04m,
        FacilityTaxRate = 0m
    };

    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public long SolarSystemId { get; init; }
    public string SolarSystemName { get; init; } = string.Empty;
    public string StructureId { get; init; } = "station";
    public string StructureName { get; init; } = "NPC Station / Neutral";
    public FacilityServiceRole ServiceRole { get; init; } = FacilityServiceRole.Manufacturing;
    public List<string> ServiceModuleIds { get; init; } = [];
    public FacilitySecurityBand SecurityBand { get; init; } = FacilitySecurityBand.HighSec;
    public List<string> RigIds { get; init; } = [];
    public decimal MaterialMultiplier { get; init; } = 1m;
    public decimal TimeMultiplier { get; init; } = 1m;
    public decimal JobCostMultiplier { get; init; } = 1m;
    public decimal SystemCostIndex { get; init; } = 0.04m;
    public decimal FacilityTaxRate { get; init; }

    public FacilityProfile ToFacilityProfile()
    {
        return new FacilityProfile
        {
            Name = Name,
            SolarSystemId = SolarSystemId,
            SolarSystemName = SolarSystemName,
            StructureId = StructureId,
            StructureName = StructureName,
            ServiceRole = ServiceRole,
            ServiceModuleIds = ServiceModuleIds,
            SecurityBand = SecurityBand,
            RigIds = RigIds,
            MaterialMultiplier = MaterialMultiplier,
            TimeMultiplier = TimeMultiplier,
            JobCostMultiplier = JobCostMultiplier,
            SystemCostIndex = SystemCostIndex,
            FacilityTaxRate = FacilityTaxRate
        };
    }
}
