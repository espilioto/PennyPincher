using ErrorOr;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PennyPincher.Contracts.EnableBanking;

namespace PennyPincher.Services.EnableBanking;

// TODO: persist as BankConnection entities once sandbox evaluation is done.
// Sandbox-only: connections stored in IMemoryCache keyed by userId, lost on restart.
public class EnableBankingService : IEnableBankingService
{
    private readonly IEnableBankingClient _client;
    private readonly IMemoryCache _cache;
    private readonly ILogger<EnableBankingService> _logger;

    public EnableBankingService(IEnableBankingClient client, IMemoryCache cache, ILogger<EnableBankingService> logger)
    {
        _client = client;
        _cache = cache;
        _logger = logger;
    }

    public async Task<ErrorOr<List<AspspDto>>> GetAspspsAsync(string? country, CancellationToken ct)
    {
        var result = await _client.GetAspspsAsync(country, ct);
        if (result.IsError)
            return result.Errors;

        return result.Value.Aspsps
            .Select(a => new AspspDto(a.Name, a.Country, a.PsuTypes, a.Beta))
            .ToList();
    }

    public async Task<ErrorOr<StartAuthResponse>> StartAuthAsync(string userId, StartAuthRequest request, CancellationToken ct)
    {
        var state = Guid.NewGuid().ToString("N");
        var result = await _client.StartAuthAsync(request.AspspName, request.AspspCountry, state, ct);
        if (result.IsError)
            return result.Errors;

        // Remember which bank this auth is for — the session response doesn't
        // echo the ASPSP, so CompleteAuth resolves it back via the state token.
        _cache.Set(PendingKey(state), new PendingAuth(request.AspspName, request.AspspCountry), TimeSpan.FromMinutes(15));
        return new StartAuthResponse(result.Value.AuthUrl, state);
    }

    public async Task<ErrorOr<CompleteAuthResponse>> CompleteAuthAsync(string userId, string code, string? state, CancellationToken ct)
    {
        var result = await _client.CreateSessionAsync(code, ct);
        if (result.IsError)
            return result.Errors;

        var session = result.Value;
        var accounts = session.Accounts
            .Select(a => new LinkedAccountDto(a.Uid, a.Iban, a.Name, a.Product, a.Currency, a.CashAccountType))
            .ToList();

        PendingAuth? pending = null;
        if (!string.IsNullOrEmpty(state))
            _cache.TryGetValue(PendingKey(state), out pending);
        var aspspName = pending?.AspspName ?? "Linked bank";
        var aspspCountry = pending?.AspspCountry ?? string.Empty;

        var connections = LoadConnections(userId);
        connections[ConnectionKey(aspspName, aspspCountry)] =
            new CachedConnection(aspspName, aspspCountry, session.SessionId, session.ValidUntil, accounts);
        SaveConnections(userId, connections);

        if (!string.IsNullOrEmpty(state))
            _cache.Remove(PendingKey(state));

        _logger.LogInformation("Connection to {Aspsp} ({Country}) cached for user {UserId}: {AccountCount} account(s), valid until {ValidUntil}",
            aspspName, aspspCountry, userId, accounts.Count, session.ValidUntil);

        return new CompleteAuthResponse(session.SessionId, session.ValidUntil, accounts);
    }

    public ErrorOr<IReadOnlyList<LinkedAccountDto>> GetCachedAccounts(string userId)
    {
        var connections = LoadConnections(userId);
        if (connections.Count == 0)
            return Error.NotFound(description: "No active Enable Banking session — link an account first");

        var accounts = connections.Values.SelectMany(c => c.Accounts).ToList();
        return ErrorOrFactory.From<IReadOnlyList<LinkedAccountDto>>(accounts);
    }

    public IReadOnlyList<BankConnectionDto> GetConnections(string userId)
    {
        return LoadConnections(userId).Values
            .OrderBy(c => c.AspspName, StringComparer.OrdinalIgnoreCase)
            .Select(c => new BankConnectionDto(c.AspspName, c.AspspCountry, c.ValidUntil, c.Accounts))
            .ToList();
    }

    public async Task<ErrorOr<List<BankBalanceDto>>> GetBalancesOverviewAsync(string userId, CancellationToken ct)
    {
        var connections = LoadConnections(userId);
        if (connections.Count == 0)
            return Error.NotFound(description: "No active Enable Banking session — link an account first");

        var results = new List<BankBalanceDto>();
        foreach (var conn in connections.Values.OrderBy(c => c.AspspName, StringComparer.OrdinalIgnoreCase))
        {
            decimal total = 0m;
            string? currency = null;
            var available = true;

            foreach (var account in conn.Accounts)
            {
                var balancesResult = await _client.GetBalancesAsync(account.Uid, ct);
                var picked = balancesResult.IsError ? null : PickRepresentative(balancesResult.Value.Balances);
                if (picked is null)
                {
                    // Can't trust a partial sum — mark the whole bank unavailable.
                    available = false;
                    break;
                }
                total += picked.Amount;
                currency ??= picked.Currency;
            }

            results.Add(available
                ? new BankBalanceDto(conn.AspspName, conn.AspspCountry, total, currency, true)
                : new BankBalanceDto(conn.AspspName, conn.AspspCountry, null, null, false));
        }

        return results;
    }

    // Enable Banking returns several balance types per account; pick the most
    // "current" one, preferring interim-available, then closing-booked, etc.
    private static EbBalanceSummary? PickRepresentative(IReadOnlyList<EbBalanceSummary> balances)
    {
        if (balances.Count == 0)
            return null;

        string[] preference = ["ITAV", "CLBD", "XPCD", "CLAV", "ITBD", "OTHR"];
        foreach (var type in preference)
        {
            var match = balances.FirstOrDefault(b => string.Equals(b.BalanceType, type, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        return balances[0];
    }

    public SessionStatusDto GetSessionStatus(string userId)
    {
        var connections = LoadConnections(userId).Values;
        if (connections.Count == 0)
            return new SessionStatusDto(false, null, 0);

        return new SessionStatusDto(
            IsLinked: true,
            ValidUntil: connections.Min(c => c.ValidUntil),
            AccountCount: connections.Sum(c => c.Accounts.Count));
    }

    public async Task<ErrorOr<List<AccountBalanceDto>>> GetBalancesAsync(string userId, string accountUid, CancellationToken ct)
    {
        if (!IsKnownAccount(userId, accountUid))
            return Error.NotFound(description: "Account not in current session");

        var result = await _client.GetBalancesAsync(accountUid, ct);
        if (result.IsError)
            return result.Errors;

        return result.Value.Balances
            .Select(b => new AccountBalanceDto(b.BalanceType, b.Amount, b.Currency, b.LastChangeDateTime))
            .ToList();
    }

    public async Task<ErrorOr<List<AccountTransactionDto>>> GetTransactionsAsync(string userId, string accountUid, DateOnly dateFrom, CancellationToken ct)
    {
        if (!IsKnownAccount(userId, accountUid))
            return Error.NotFound(description: "Account not in current session");

        var result = await _client.GetTransactionsAsync(accountUid, dateFrom, ct);
        if (result.IsError)
            return result.Errors;

        var mapped = result.Value.Transactions
            .Select(t => new AccountTransactionDto(
                t.EntryReference,
                t.Amount,
                t.Currency,
                t.CreditDebitIndicator,
                t.Status,
                t.BookingDate,
                t.TransactionDate,
                t.ValueDate,
                t.RemittanceInformation,
                t.CreditorName,
                t.DebtorName,
                t.MerchantCategoryCode,
                t.BalanceAfterTransaction))
            .ToList();

        _logger.LogInformation(
            "Transaction evaluation for {Account}: total={Total}, withRemittance={WithRemittance}, withMcc={WithMcc}, withBalanceAfter={WithBalanceAfter}",
            accountUid,
            mapped.Count,
            mapped.Count(x => !string.IsNullOrWhiteSpace(x.RemittanceInformation)),
            mapped.Count(x => !string.IsNullOrWhiteSpace(x.MerchantCategoryCode)),
            mapped.Count(x => x.BalanceAfterTransaction.HasValue));

        return mapped;
    }

    private bool IsKnownAccount(string userId, string accountUid)
        => LoadConnections(userId).Values.SelectMany(c => c.Accounts).Any(a => a.Uid == accountUid);

    private Dictionary<string, CachedConnection> LoadConnections(string userId)
        => _cache.TryGetValue<Dictionary<string, CachedConnection>>(ConnectionsKey(userId), out var connections) && connections is not null
            ? connections
            : new Dictionary<string, CachedConnection>();

    private void SaveConnections(string userId, Dictionary<string, CachedConnection> connections)
    {
        // Keep the store alive as long as the longest-lived connection; expired
        // connections stay visible (flagged in the UI) so the user can re-link.
        var latest = connections.Values.Max(c => c.ValidUntil);
        _cache.Set(ConnectionsKey(userId), connections, latest);
    }

    private static string ConnectionsKey(string userId) => $"eb:connections:{userId}";
    private static string PendingKey(string state) => $"eb:pending:{state}";
    private static string ConnectionKey(string aspspName, string aspspCountry) => $"{aspspName}|{aspspCountry}";

    private record CachedConnection(string AspspName, string AspspCountry, string SessionId, DateTimeOffset ValidUntil, List<LinkedAccountDto> Accounts);
    private record PendingAuth(string AspspName, string AspspCountry);
}
