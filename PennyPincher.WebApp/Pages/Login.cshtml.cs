using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PennyPincher.WebApp.Pages;

public class LoginModel : PageModel
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public LoginModel(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    [BindProperty]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty(Name = "cf-turnstile-response")]
    public string? TurnstileToken { get; set; }

    public string TurnstileSiteKey => _configuration["Turnstile:SiteKey"] ?? "";

    public string? ErrorMessage { get; set; }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToPage("/Index");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Validate Turnstile token before checking credentials
        var secretKey = _configuration["Turnstile:SecretKey"];
        if (!string.IsNullOrEmpty(secretKey))
        {
            var turnstileValid = await ValidateTurnstileAsync(secretKey);
            if (!turnstileValid)
            {
                ErrorMessage = "Verification failed. Please try again.";
                return Page();
            }
        }

        var client = _httpClientFactory.CreateClient("PennyPincherApi");

        var loginPayload = new { Email, Password };
        var response = await client.PostAsJsonAsync("api/Users/login", loginPayload);

        if (!response.IsSuccessStatusCode)
        {
            ErrorMessage = "Invalid email or password.";
            return Page();
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = json.GetProperty("token").GetString()!;

        Response.Cookies.Append("jwt", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddHours(1)
        });

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, Email),
            new(ClaimTypes.Email, Email)
        };
        var identity = new ClaimsIdentity(claims, "Cookies");
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync("Cookies", principal);

        return RedirectToPage("/Statements/Index");
    }

    private async Task<bool> ValidateTurnstileAsync(string secretKey)
    {
        if (string.IsNullOrEmpty(TurnstileToken))
            return false;

        var client = _httpClientFactory.CreateClient("Turnstile");
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["secret"] = secretKey,
            ["response"] = TurnstileToken,
            ["remoteip"] = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        var response = await client.PostAsync("/turnstile/v0/siteverify", content);

        if (!response.IsSuccessStatusCode)
            return false;

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        return result.GetProperty("success").GetBoolean();
    }
}
