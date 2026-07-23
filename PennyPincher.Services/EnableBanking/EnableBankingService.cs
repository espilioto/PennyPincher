using ErrorOr;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PennyPincher.Contracts.EnableBanking;

namespace PennyPincher.Services.EnableBanking;

// TODO: persist as BankConnection entity once sandbox evaluation is done.
// Sandbox-only: session stored in IMemoryCache keyed by userId, lost on restart.
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

    public async Task<ErrorOr<StartAuthResponse>> StartAuthAsync(string userId, StartAuthRequest request, CancellationToken ct)
    {
        var state = Guid.NewGuid().ToString("N");
        var result = await _client.StartAuthAsync(request.AspspName, request.AspspCountry, state, ct);
        if (result.IsError)
            return result.Errors;

        _cache.Set(PendingStateKey(userId), state, TimeSpan.FromMinutes(15));
        return new StartAuthResponse(result.Value.AuthUrl, state);
    }

    public async Task<ErrorOr<CompleteAuthResponse>> CompleteAuthAsync(string userId, string code, CancellationToken ct)
    {
        var result = await _client.CreateSessionAsync(code, ct);
        if (result.IsError)
            return result.Errors;

        var session = result.Value;
        var accounts = session.Accounts
            .Select(a => new LinkedAccountDto(a.Uid, a.Iban, a.Name, a.Product, a.Currency, a.CashAccountType))
            .ToList();

        var entry = new CachedSession(session.SessionId, session.ValidUntil, accounts);
        _cache.Set(SessionKey(userId), entry, session.ValidUntil);
        _logger.LogInformation("Session {SessionId} cached for user {UserId} with {AccountCount} accounts, valid until {ValidUntil}",
            session.SessionId, userId, accounts.Count, session.ValidUntil);

        return new CompleteAuthResponse(session.SessionId, session.ValidUntil, accounts);
    }

    public ErrorOr<IReadOnlyList<LinkedAccountDto>> GetCachedAccounts(string userId)
    {
        if (!_cache.TryGetValue<CachedSession>(SessionKey(userId), out var session) || session is null)
            return Error.NotFound(description: "No active Enable Banking session — link an account first");
        return ErrorOrFactory.From<IReadOnlyList<LinkedAccountDto>>(session.Accounts);
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
    {
        if (!_cache.TryGetValue<CachedSession>(SessionKey(userId), out var session) || session is null)
            return false;
        return session.Accounts.Any(a => a.Uid == accountUid);
    }

    private static string SessionKey(string userId) => $"eb:session:{userId}";
    private static string PendingStateKey(string userId) => $"eb:state:{userId}";

    private record CachedSession(string SessionId, DateTimeOffset ValidUntil, List<LinkedAccountDto> Accounts);
}
