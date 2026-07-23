using ErrorOr;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PennyPincher.Services.EnableBanking.Models;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace PennyPincher.Services.EnableBanking;

public interface IEnableBankingClient
{
    Task<ErrorOr<AuthStartResult>> StartAuthAsync(string aspspName, string aspspCountry, string state, CancellationToken ct);
    Task<ErrorOr<SessionResult>> CreateSessionAsync(string code, CancellationToken ct);
    Task<ErrorOr<BalancesResult>> GetBalancesAsync(string accountUid, CancellationToken ct);
    Task<ErrorOr<TransactionsResult>> GetTransactionsAsync(string accountUid, DateOnly dateFrom, CancellationToken ct);
}

public record AuthStartResult(string AuthUrl);
public record SessionResult(string SessionId, DateTimeOffset ValidUntil, List<EbAccountSummary> Accounts);
public record EbAccountSummary(string Uid, string? Iban, string? Name, string? Product, string? Currency, string? CashAccountType);
public record BalancesResult(List<EbBalanceSummary> Balances);
public record EbBalanceSummary(string BalanceType, decimal Amount, string Currency, DateTimeOffset? LastChangeDateTime);
public record TransactionsResult(List<EbTransactionSummary> Transactions);
public record EbTransactionSummary(
    string? EntryReference,
    decimal Amount,
    string Currency,
    string CreditDebitIndicator,
    string Status,
    DateOnly? BookingDate,
    DateOnly? TransactionDate,
    DateOnly? ValueDate,
    string? RemittanceInformation,
    string? CreditorName,
    string? DebtorName,
    string? MerchantCategoryCode,
    decimal? BalanceAfterTransaction);

public class EnableBankingClient : IEnableBankingClient
{
    public const string HttpClientName = "EnableBanking";

    private readonly HttpClient _http;
    private readonly IEnableBankingJwtFactory _jwtFactory;
    private readonly EnableBankingOptions _options;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<EnableBankingClient> _logger;

    public EnableBankingClient(
        IHttpClientFactory httpClientFactory,
        IEnableBankingJwtFactory jwtFactory,
        IOptions<EnableBankingOptions> options,
        IHttpContextAccessor httpContextAccessor,
        ILogger<EnableBankingClient> logger)
    {
        _http = httpClientFactory.CreateClient(HttpClientName);
        _jwtFactory = jwtFactory;
        _options = options.Value;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<ErrorOr<AuthStartResult>> StartAuthAsync(string aspspName, string aspspCountry, string state, CancellationToken ct)
    {
        try
        {
            var validUntil = DateTimeOffset.UtcNow.AddDays(180).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var body = new AuthStartBody(
                new AccessSpec(validUntil),
                new AspspSpec(aspspName, aspspCountry),
                state,
                _options.RedirectUri,
                "personal");

            using var req = BuildRequest(HttpMethod.Post, "auth", body);
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return await ToErrorAsync(resp, "auth/start", ct);

            var parsed = await resp.Content.ReadFromJsonAsync<AuthStartResponse>(cancellationToken: ct);
            if (parsed is null || string.IsNullOrEmpty(parsed.Url))
                return Error.Unexpected(description: "Empty response from POST /auth");

            return new AuthStartResult(parsed.Url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StartAuth failed");
            return Error.Unexpected(description: ex.Message);
        }
    }

    public async Task<ErrorOr<SessionResult>> CreateSessionAsync(string code, CancellationToken ct)
    {
        try
        {
            using var req = BuildRequest(HttpMethod.Post, "sessions", new SessionsBody(code));
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return await ToErrorAsync(resp, "sessions", ct);

            var parsed = await resp.Content.ReadFromJsonAsync<SessionsResponse>(cancellationToken: ct);
            if (parsed is null)
                return Error.Unexpected(description: "Empty response from POST /sessions");

            var accounts = parsed.Accounts.Select(a => new EbAccountSummary(
                a.Uid,
                a.AccountId?.Iban ?? a.AccountId?.Other?.Identification,
                a.Name,
                a.Product,
                a.Currency,
                a.CashAccountType
            )).ToList();

            // valid_until nests inside `access`. Fall back to now+180 days if the bank omits it.
            var validUntil = parsed.Access?.ValidUntil ?? DateTimeOffset.UtcNow.AddDays(180);
            return new SessionResult(parsed.SessionId, validUntil, accounts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateSession failed");
            return Error.Unexpected(description: ex.Message);
        }
    }

    public async Task<ErrorOr<BalancesResult>> GetBalancesAsync(string accountUid, CancellationToken ct)
    {
        try
        {
            using var req = BuildRequest(HttpMethod.Get, $"accounts/{Uri.EscapeDataString(accountUid)}/balances", body: null);
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return await ToErrorAsync(resp, "balances", ct);

            var parsed = await resp.Content.ReadFromJsonAsync<BalancesResponse>(cancellationToken: ct);
            if (parsed is null)
                return Error.Unexpected(description: "Empty response from GET /balances");

            var summaries = parsed.Balances.Select(b => new EbBalanceSummary(
                b.BalanceType,
                decimal.Parse(b.BalanceAmount.Amount, System.Globalization.CultureInfo.InvariantCulture),
                b.BalanceAmount.Currency,
                b.LastChangeDateTime
            )).ToList();

            return new BalancesResult(summaries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetBalances failed");
            return Error.Unexpected(description: ex.Message);
        }
    }

    public async Task<ErrorOr<TransactionsResult>> GetTransactionsAsync(string accountUid, DateOnly dateFrom, CancellationToken ct)
    {
        try
        {
            var all = new List<EbTransactionSummary>();
            string? continuationKey = null;
            int pageCount = 0;

            do
            {
                var url = $"accounts/{Uri.EscapeDataString(accountUid)}/transactions?date_from={dateFrom:yyyy-MM-dd}";
                if (!string.IsNullOrEmpty(continuationKey))
                    url += $"&continuation_key={Uri.EscapeDataString(continuationKey)}";

                using var req = BuildRequest(HttpMethod.Get, url, body: null);
                using var resp = await _http.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode)
                    return await ToErrorAsync(resp, "transactions", ct);

                var parsed = await resp.Content.ReadFromJsonAsync<TransactionsResponse>(cancellationToken: ct);
                if (parsed is null)
                    return Error.Unexpected(description: "Empty response from GET /transactions");

                pageCount++;
                all.AddRange(parsed.Transactions.Select(MapTransaction));
                continuationKey = parsed.ContinuationKey;
            } while (!string.IsNullOrEmpty(continuationKey));

            _logger.LogInformation("Fetched {Count} transactions for {Account} across {Pages} page(s)", all.Count, accountUid, pageCount);
            return new TransactionsResult(all);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetTransactions failed");
            return Error.Unexpected(description: ex.Message);
        }
    }

    private static EbTransactionSummary MapTransaction(EbTransaction t)
    {
        var amount = decimal.Parse(t.TransactionAmount.Amount, System.Globalization.CultureInfo.InvariantCulture);
        var remittance = t.RemittanceInformation is { Count: > 0 }
            ? string.Join(" ", t.RemittanceInformation.Where(s => !string.IsNullOrWhiteSpace(s)))
            : null;
        decimal? balanceAfter = t.BalanceAfterTransaction is null
            ? null
            : decimal.Parse(t.BalanceAfterTransaction.Amount, System.Globalization.CultureInfo.InvariantCulture);

        return new EbTransactionSummary(
            t.EntryReference,
            amount,
            t.TransactionAmount.Currency,
            t.CreditDebitIndicator,
            t.Status,
            t.BookingDate,
            t.TransactionDate,
            t.ValueDate,
            remittance,
            t.Creditor?.Name,
            t.Debtor?.Name,
            t.MerchantCategoryCode,
            balanceAfter);
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string relativeUrl, object? body)
    {
        var req = new HttpRequestMessage(method, relativeUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _jwtFactory.Create());

        // PSD2 "PSU online" context — real banks (Piraeus observed) reject
        // transaction queries as ASPSP_ERROR without these headers.
        // Prefer the forwarded X-Psu-* headers set by the WebApp's delegating
        // handler (real browser IP/UA), fall back to the API's inbound
        // connection context.
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx is not null)
        {
            var ip = ctx.Request.Headers.TryGetValue("X-Psu-Ip", out var fwdIp) && !string.IsNullOrWhiteSpace(fwdIp.ToString())
                ? fwdIp.ToString()
                : ctx.Connection.RemoteIpAddress?.ToString();
            if (!string.IsNullOrEmpty(ip))
                req.Headers.TryAddWithoutValidation("PSU-IP-Address", ip);

            var ua = ctx.Request.Headers.TryGetValue("X-Psu-User-Agent", out var fwdUa) && !string.IsNullOrWhiteSpace(fwdUa.ToString())
                ? fwdUa.ToString()
                : ctx.Request.Headers.UserAgent.ToString();
            if (!string.IsNullOrWhiteSpace(ua))
                req.Headers.TryAddWithoutValidation("PSU-User-Agent", ua);
        }

        if (body is not null)
            req.Content = JsonContent.Create(body);
        return req;
    }

    private static async Task<Error> ToErrorAsync(HttpResponseMessage resp, string op, CancellationToken ct)
    {
        var text = await resp.Content.ReadAsStringAsync(ct);
        return Error.Failure(
            code: $"EnableBanking.{op}.{(int)resp.StatusCode}",
            description: $"Enable Banking {op} failed ({(int)resp.StatusCode}): {text}");
    }
}
