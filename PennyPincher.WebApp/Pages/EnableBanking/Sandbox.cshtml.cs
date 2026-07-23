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
    public string? ErrorMessage { get; set; }

    // Mock ASPSPs exposed by Enable Banking's sandbox.
    // Source: Enable Banking Control Panel → Sandbox → connectors.
    public static readonly (string Name, string Country, string Label)[] MockAspsps =
    [
        ("Nordea", "FI", "Nordea (Mock FI)"),
        ("OP", "FI", "OP (Mock FI)"),
        ("Danske Bank", "FI", "Danske Bank (Mock FI)"),
        ("SEB", "SE", "SEB (Mock SE)"),
        ("Swedbank", "SE", "Swedbank (Mock SE)")
    ];

    public async Task OnGetAsync()
    {
        var client = _httpClientFactory.CreateClient("PennyPincherApi");
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
