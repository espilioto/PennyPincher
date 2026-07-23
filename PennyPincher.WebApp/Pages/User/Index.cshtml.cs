using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using PennyPincher.Contracts.EnableBanking;
using PennyPincher.Contracts.Users;

namespace PennyPincher.WebApp.Pages.User;

[Authorize]
public class IndexModel : PageModel
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(IHttpClientFactory httpClientFactory, ILogger<IndexModel> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public List<BankConnectionDto> Connections { get; set; } = [];
    public List<AspspDto> Banks { get; set; } = [];
    public string? BankErrorMessage { get; set; }

    // Two-letter country filter for the bank catalog. Defaults to Greece.
    [BindProperty(SupportsGet = true)]
    public string Country { get; set; } = "GR";

    // Enable Banking's supported markets (enablebanking.com/docs/markets).
    public static readonly (string Code, string Name)[] Countries =
    [
        ("AT", "Austria"),
        ("BE", "Belgium"),
        ("BG", "Bulgaria"),
        ("HR", "Croatia"),
        ("CY", "Cyprus"),
        ("CZ", "Czechia"),
        ("DK", "Denmark"),
        ("EE", "Estonia"),
        ("FI", "Finland"),
        ("FR", "France"),
        ("DE", "Germany"),
        ("GR", "Greece"),
        ("HU", "Hungary"),
        ("IS", "Iceland"),
        ("IE", "Ireland"),
        ("IT", "Italy"),
        ("LV", "Latvia"),
        ("LI", "Liechtenstein"),
        ("LT", "Lithuania"),
        ("LU", "Luxembourg"),
        ("MT", "Malta"),
        ("NL", "Netherlands"),
        ("NO", "Norway"),
        ("PL", "Poland"),
        ("PT", "Portugal"),
        ("RO", "Romania"),
        ("SK", "Slovakia"),
        ("SI", "Slovenia"),
        ("ES", "Spain"),
        ("SE", "Sweden"),
        ("GB", "United Kingdom"),
    ];

    public IEnumerable<SelectListItem> CountryItems =>
        Countries.Select(c => new SelectListItem($"{c.Name} ({c.Code})", c.Code));

    public async Task OnGetAsync()
    {
        var client = _httpClientFactory.CreateClient("PennyPincherApi");

        var country = string.IsNullOrWhiteSpace(Country) ? null : Country.Trim().ToUpperInvariant();
        var banksUrl = country is null
            ? "api/enablebanking/aspsps"
            : $"api/enablebanking/aspsps?country={Uri.EscapeDataString(country)}";
        var banksResp = await client.GetAsync(banksUrl);
        if (banksResp.IsSuccessStatusCode)
            Banks = await banksResp.Content.ReadFromJsonAsync<List<AspspDto>>() ?? [];
        else
        {
            _logger.LogError("Failed to load bank list ({Status}): {Body}",
                (int)banksResp.StatusCode, await banksResp.Content.ReadAsStringAsync());
            BankErrorMessage = "Couldn't load the list of banks. Please try again in a moment.";
        }

        var connResp = await client.GetAsync("api/enablebanking/connections");
        if (connResp.IsSuccessStatusCode)
            Connections = await connResp.Content.ReadFromJsonAsync<List<BankConnectionDto>>() ?? [];
    }

    public async Task<IActionResult> OnPostLinkAsync(string aspspName, string aspspCountry)
    {
        var client = _httpClientFactory.CreateClient("PennyPincherApi");
        var resp = await client.PostAsJsonAsync(
            "api/enablebanking/auth/start",
            new StartAuthRequest(aspspName, aspspCountry));

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to start bank link for {Bank}/{Country} ({Status}): {Body}",
                aspspName, aspspCountry, (int)resp.StatusCode, await resp.Content.ReadAsStringAsync());
            BankErrorMessage = $"Couldn't start linking {aspspName}. Please try again.";
            await OnGetAsync();
            return Page();
        }

        var payload = await resp.Content.ReadFromJsonAsync<StartAuthResponse>();
        if (payload is null)
        {
            _logger.LogError("Empty response from auth/start for {Bank}/{Country}", aspspName, aspspCountry);
            BankErrorMessage = $"Couldn't start linking {aspspName}. Please try again.";
            await OnGetAsync();
            return Page();
        }

        return Redirect(payload.AuthUrl);
    }

    public async Task<IActionResult> OnPostChangePasswordAsync([FromBody] ChangePasswordRequest request)
    {
        var client = _httpClientFactory.CreateClient("PennyPincherApi");
        var response = await client.PutAsJsonAsync("api/users/password", request);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            return new ContentResult { Content = body, ContentType = "application/json", StatusCode = (int)response.StatusCode };
        }

        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnPostDeleteAccountAsync([FromBody] DeleteAccountRequest request)
    {
        var client = _httpClientFactory.CreateClient("PennyPincherApi");
        var apiRequest = new HttpRequestMessage(HttpMethod.Delete, "api/users")
        {
            Content = JsonContent.Create(request)
        };
        var response = await client.SendAsync(apiRequest);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            return new ContentResult { Content = body, ContentType = "application/json", StatusCode = (int)response.StatusCode };
        }

        await HttpContext.SignOutAsync("Cookies");
        Response.Cookies.Delete("jwt");
        return new JsonResult(new { success = true });
    }
}
