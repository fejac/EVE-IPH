using System.Windows;

namespace EveIndustryPlanner.App;

public partial class LoginDialog : Window
{
    private static readonly IReadOnlyList<string> DefaultCharacterScopes =
    [
        "esi-skills.read_skills.v1",
        "esi-universe.read_structures.v1",
        "esi-markets.structure_markets.v1",
        "esi-characters.read_standings.v1",
        "esi-industry.read_character_jobs.v1",
        "esi-characters.read_agents_research.v1",
        "esi-assets.read_assets.v1",
        "esi-characters.read_blueprints.v1",
        "esi-planets.manage_planets.v1",
        "esi-corporations.read_corporation_membership.v1",
        "esi-industry.read_corporation_jobs.v1",
        "esi-assets.read_corporation_assets.v1",
        "esi-corporations.read_blueprints.v1",
        "esi-wallet.read_corporation_wallets.v1",
        "esi-markets.read_corporation_orders.v1",
        "esi-corporations.read_divisions.v1",
        "esi-location.read_ship_type.v1",
        "esi-location.read_location.v1",
        "esi-characters.read_loyalty.v1",
        "esi-wallet.read_character_wallet.v1",
        "esi-markets.read_character_orders.v1",
        "esi-ui.open_window.v1"
    ];

    private readonly EveSsoCharacterAuthService authService = new();
    private readonly UserSettingsService settingsService = new(UserSettingsService.GetDefaultSettingsFilePath());
    private readonly UserSettings settings;

    public LoginDialog()
    {
        InitializeComponent();
        settings = settingsService.Load();
        PocketBaseUrlTextBox.Text = settings.PocketBaseUrl;
        StatusTextBlock.Text = "Enter the PocketBase server URL and sign in.";
    }

    public SavedCharacterAccount? AuthenticatedAccount { get; private set; }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        var pocketBaseUrl = PocketBaseUrlTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(pocketBaseUrl))
        {
            StatusTextBlock.Text = "PocketBase URL is required.";
            return;
        }

        LoginButton.IsEnabled = false;
        PocketBaseUrlTextBox.IsEnabled = false;
        StatusTextBlock.Text = "Waiting for EVE SSO login.";

        try
        {
            var token = await authService.AddCharacterViaPocketBaseAsync(pocketBaseUrl, DefaultCharacterScopes);
            AuthenticatedAccount = ToSavedAccount(token);
            SaveAuthenticatedAccount(pocketBaseUrl, AuthenticatedAccount);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Login failed: {ex.Message}";
            LoginButton.IsEnabled = true;
            PocketBaseUrlTextBox.IsEnabled = true;
        }
    }

    private static SavedCharacterAccount ToSavedAccount(EveSsoCharacterToken token)
    {
        return new SavedCharacterAccount
        {
            CharacterId = token.CharacterId,
            CharacterName = token.CharacterName,
            AccessToken = token.AccessToken,
            RefreshToken = token.RefreshToken,
            TokenType = token.TokenType,
            Scopes = token.Scopes.ToList(),
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, token.ExpiresIn)),
            AddedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private void SaveAuthenticatedAccount(string pocketBaseUrl, SavedCharacterAccount account)
    {
        var accounts = settings.CharacterAccounts
            .Where(existing => existing.CharacterId != account.CharacterId)
            .ToList();

        var existingAddedAt = settings.CharacterAccounts
            .FirstOrDefault(existing => existing.CharacterId == account.CharacterId)
            ?.AddedAt;

        accounts.Add(new SavedCharacterAccount
        {
            CharacterId = account.CharacterId,
            CharacterName = account.CharacterName,
            AccessToken = account.AccessToken,
            RefreshToken = account.RefreshToken,
            TokenType = account.TokenType,
            Scopes = account.Scopes,
            AccessTokenExpiresAt = account.AccessTokenExpiresAt,
            AddedAt = existingAddedAt ?? account.AddedAt,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        settingsService.Save(new UserSettings
        {
            FacilityProfiles = settings.FacilityProfiles,
            CharacterAccounts = accounts.OrderBy(saved => saved.CharacterName, StringComparer.OrdinalIgnoreCase).ToList(),
            FinalProductFacilityId = settings.FinalProductFacilityId,
            ComponentFacilityId = settings.ComponentFacilityId,
            ReactionFacilityId = settings.ReactionFacilityId,
            MaterialMarketLocationId = settings.MaterialMarketLocationId,
            ProductMarketLocationId = settings.ProductMarketLocationId,
            MaterialPriceSelection = settings.MaterialPriceSelection,
            ProductPriceSelection = settings.ProductPriceSelection,
            EnableBuildBuy = settings.EnableBuildBuy,
            BuildBuyDepth = settings.BuildBuyDepth,
            MaxBuildBuyDepth = settings.MaxBuildBuyDepth,
            PocketBaseUrl = pocketBaseUrl
        });
    }
}
