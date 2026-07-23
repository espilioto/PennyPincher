using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PennyPincher.Contracts.EnableBanking;

namespace PennyPincher.WebApp.Pages.EnableBanking;

[Authorize]
public class CallbackModel : PageModel
{
    private readonly IHttpClientFactory _httpClientFactory;

    public CallbackModel(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public string? ErrorMessage { get; set; }
    public CompleteAuthResponse? Session { get; set; }

    public async Task OnGetAsync(string? code, string? state, string? error)
    {
        if (!string.IsNullOrEmpty(error))
        {
            ErrorMessage = $"Bank returned error: {error}";
            return;
        }
        if (string.IsNullOrEmpty(code))
        {
            ErrorMessage = "Missing `code` query parameter from bank redirect.";
            return;
        }

        var client = _httpClientFactory.CreateClient("PennyPincherApi");
        var resp = await client.PostAsJsonAsync("api/enablebanking/auth/complete", new CompleteAuthRequest(code));
        if (!resp.IsSuccessStatusCode)
        {
            ErrorMessage = $"Session exchange failed ({(int)resp.StatusCode}): {await resp.Content.ReadAsStringAsync()}";
            return;
        }

        Session = await resp.Content.ReadFromJsonAsync<CompleteAuthResponse>();
    }
}
