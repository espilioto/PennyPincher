using System.Net.Http.Headers;

namespace PennyPincher.WebApp.Auth;

public class JwtCookieDelegatingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public JwtCookieDelegatingHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var ctx = _httpContextAccessor.HttpContext;

        var token = ctx?.Request.Cookies["jwt"];
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        // Forward the browser's real IP and UA so server-to-server calls
        // downstream (Enable Banking) can use them as PSD2 PSU context.
        if (ctx is not null)
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            if (!string.IsNullOrEmpty(ip))
                request.Headers.TryAddWithoutValidation("X-Psu-Ip", ip);

            var ua = ctx.Request.Headers.UserAgent.ToString();
            if (!string.IsNullOrWhiteSpace(ua))
                request.Headers.TryAddWithoutValidation("X-Psu-User-Agent", ua);
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new ApiUnauthorizedException();
        }

        return response;
    }
}
