using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PennyPincher.Contracts.EnableBanking;

namespace PennyPincher.WebApp.Pages.EnableBanking;

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

    public List<LinkedAccountDto> Accounts { get; set; } = [];
    public List<AspspDto> Banks { get; set; } = [];
    public string? ErrorMessage { get; set; }

    // Two-letter country filter for the bank catalog. Defaults to Greece.
    [BindProperty(SupportsGet = true)]
    public string Country { get; set; } = "GR";

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
            ErrorMessage = "Couldn't load the list of banks. Please try again in a moment.";
        }

        var resp = await client.GetAsync("api/enablebanking/accounts");
        if (resp.IsSuccessStatusCode)
            Accounts = await resp.Content.ReadFromJsonAsync<List<LinkedAccountDto>>() ?? [];
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
            ErrorMessage = $"Couldn't start linking {aspspName}. Please try again.";
            await OnGetAsync();
            return Page();
        }

        var payload = await resp.Content.ReadFromJsonAsync<StartAuthResponse>();
        if (payload is null)
        {
            _logger.LogError("Empty response from auth/start for {Bank}/{Country}", aspspName, aspspCountry);
            ErrorMessage = $"Couldn't start linking {aspspName}. Please try again.";
            await OnGetAsync();
            return Page();
        }

        return Redirect(payload.AuthUrl);
    }
}
