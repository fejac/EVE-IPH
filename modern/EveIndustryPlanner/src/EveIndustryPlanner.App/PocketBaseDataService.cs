using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EveIndustryPlanner.App.ViewModels;
using EveIndustryPlanner.Core;

namespace EveIndustryPlanner.App;

public sealed class PocketBaseDataService(Func<string> pocketBaseUrlProvider, Func<string> authTokenProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient httpClient = new();

    public async Task<PocketBaseDataSnapshot?> LoadSnapshotAsync(CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, "/api/eve-industry/data/snapshot");
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<PocketBaseDataSnapshot>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public Task SaveSettingsAsync(ServerUserSettings settings, CancellationToken cancellationToken = default)
    {
        return SendJsonAsync(HttpMethod.Put, "/api/eve-industry/data/settings", new { settings }, cancellationToken);
    }

    public Task SaveFacilitiesAsync(IReadOnlyList<SavedFacilityProfile> facilities, CancellationToken cancellationToken = default)
    {
        return SendJsonAsync(HttpMethod.Put, "/api/eve-industry/data/facilities", new { facilities }, cancellationToken);
    }

    public async Task<string> SaveProductionLedgerAsync(string ledgerId, string name, ServerProductionLedgerDocument ledger, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Put, "/api/eve-industry/data/production-ledger");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { id = ledgerId, name, ledger }, JsonOptions),
            Encoding.UTF8,
            "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var saveResponse = await JsonSerializer.DeserializeAsync<SaveRecordResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        return saveResponse?.Id ?? ledgerId;
    }

    public async Task DeleteProductionLedgerAsync(string ledgerId, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Delete, $"/api/eve-industry/data/production-ledger/{Uri.EscapeDataString(ledgerId)}");
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public Task SaveMarketScanCacheAsync(string cacheKey, DateTimeOffset expiresAt, IReadOnlyList<MainWindowViewModel.MarketScannerResultRow> rows, CancellationToken cancellationToken = default)
    {
        return SendJsonAsync(
            HttpMethod.Put,
            "/api/eve-industry/data/market-scan-cache",
            new
            {
                cache_key = cacheKey,
                expires_at = expiresAt,
                rows
            },
            cancellationToken);
    }

    private async Task SendJsonAsync(HttpMethod method, string path, object payload, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(responseText))
        {
            response.EnsureSuccessStatusCode();
        }

        try
        {
            using var json = JsonDocument.Parse(responseText);
            if (json.RootElement.TryGetProperty("message", out var message))
            {
                throw new HttpRequestException(message.GetString() ?? responseText);
            }
        }
        catch (JsonException)
        {
        }

        throw new HttpRequestException(responseText);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var baseUrl = pocketBaseUrlProvider().Trim();
        var authToken = authTokenProvider().Trim();
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(authToken))
        {
            throw new InvalidOperationException("PocketBase server URL or auth token is not configured.");
        }

        var request = new HttpRequestMessage(method, new Uri(new Uri(EnsureTrailingSlash(baseUrl)), path.TrimStart('/')));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
        return request;
    }

    private static string EnsureTrailingSlash(string value)
    {
        return value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";
    }
}

public sealed class PocketBaseDataSnapshot
{
    [JsonPropertyName("settings")]
    public ServerUserSettings? Settings { get; init; }

    [JsonPropertyName("facilities")]
    public List<SavedFacilityProfile> Facilities { get; init; } = [];

    [JsonPropertyName("production_ledgers")]
    public List<ServerProductionLedgerRecord> ProductionLedgers { get; init; } = [];

    [JsonPropertyName("market_scan_cache")]
    public List<ServerMarketScannerCacheEntry> MarketScanCache { get; init; } = [];
}

public sealed class ServerUserSettings
{
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
}

public sealed class ServerProductionLedgerDocument
{
    public DateTimeOffset SavedAt { get; init; }
    public MainWindowViewModel.ProductionPlannerSettings Settings { get; init; } = new();
    public List<MainWindowViewModel.ProductionLedgerEntry> Entries { get; init; } = [];
    public List<MainWindowViewModel.ProductionJobCompletionState> JobStates { get; init; } = [];
}

public sealed class ServerProductionLedgerRecord
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = "Production Ledger";

    [JsonPropertyName("ledger")]
    public ServerProductionLedgerDocument Ledger { get; init; } = new();
}

public sealed class SaveRecordResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;
}

public sealed class ServerMarketScannerCacheEntry
{
    [JsonPropertyName("cache_key")]
    public string CacheKey { get; init; } = string.Empty;

    [JsonPropertyName("expires_at")]
    public DateTimeOffset ExpiresAt { get; init; }

    [JsonPropertyName("rows")]
    public List<MainWindowViewModel.MarketScannerResultRow> Rows { get; init; } = [];
}
