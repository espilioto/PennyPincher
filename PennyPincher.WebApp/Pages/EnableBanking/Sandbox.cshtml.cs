using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PennyPincher.Contracts.EnableBanking;

namespace PennyPincher.WebApp.Pages.EnableBanking;

[Authorize]
public class SandboxModel : PageModel
{
    private readonly IHttpClientFactory _httpClientFactory;

    public SandboxModel(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public List<LinkedAccountDto> Accounts { get; set; } = [];
    public List<AspspDto> Aspsps { get; set; } = [];
    public string? ErrorMessage { get; set; }

    // Two-letter country filter for the live ASPSP catalog. Defaults to Greece.
    [BindProperty(SupportsGet = true)]
    public string Country { get; set; } = "GR";

    public async Task OnGetAsync()
    {
        var client = _httpClientFactory.CreateClient("PennyPincherApi");

        var country = string.IsNullOrWhiteSpace(Country) ? null : Country.Trim().ToUpperInvariant();
        var aspspUrl = country is null
            ? "api/enablebanking/aspsps"
            : $"api/enablebanking/aspsps?country={Uri.EscapeDataString(country)}";
        var aspspResp = await client.GetAsync(aspspUrl);
        if (aspspResp.IsSuccessStatusCode)
            Aspsps = await aspspResp.Content.ReadFromJsonAsync<List<AspspDto>>() ?? [];
        else
            ErrorMessage = $"Failed to load ASPSP list ({(int)aspspResp.StatusCode}): {await aspspResp.Content.ReadAsStringAsync()}";

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
            ErrorMessage = $"Failed to start auth ({(int)resp.StatusCode}): {await resp.Content.ReadAsStringAsync()}";
            await OnGetAsync();
            return Page();
        }

        var payload = await resp.Content.ReadFromJsonAsync<StartAuthResponse>();
        if (payload is null)
        {
            ErrorMessage = "Empty response from auth/start";
            await OnGetAsync();
            return Page();
        }

        return Redirect(payload.AuthUrl);
    }

    public async Task<IActionResult> OnGetBalancesAsync(string accountUid)
    {
        var client = _httpClientFactory.CreateClient("PennyPincherApi");
        var resp = await client.GetAsync($"api/enablebanking/accounts/{Uri.EscapeDataString(accountUid)}/balances");
        return await ForwardJsonAsync(resp);
    }

    public async Task<IActionResult> OnGetTransactionsAsync(string accountUid)
    {
        var client = _httpClientFactory.CreateClient("PennyPincherApi");
        var resp = await client.GetAsync($"api/enablebanking/accounts/{Uri.EscapeDataString(accountUid)}/transactions");
        return await ForwardJsonAsync(resp);
    }

    private static async Task<IActionResult> ForwardJsonAsync(HttpResponseMessage resp)
    {
        var body = await resp.Content.ReadAsStringAsync();
        return new ContentResult
        {
            Content = body,
            ContentType = "application/json",
            StatusCode = (int)resp.StatusCode
        };
    }
}
