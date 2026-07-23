using ErrorOr;
using PennyPincher.Contracts.EnableBanking;

namespace PennyPincher.Services.EnableBanking;

public interface IEnableBankingService
{
    Task<ErrorOr<StartAuthResponse>> StartAuthAsync(string userId, StartAuthRequest request, CancellationToken ct);
    Task<ErrorOr<CompleteAuthResponse>> CompleteAuthAsync(string userId, string code, CancellationToken ct);
    ErrorOr<IReadOnlyList<LinkedAccountDto>> GetCachedAccounts(string userId);
    Task<ErrorOr<List<AccountBalanceDto>>> GetBalancesAsync(string userId, string accountUid, CancellationToken ct);
    Task<ErrorOr<List<AccountTransactionDto>>> GetTransactionsAsync(string userId, string accountUid, DateOnly dateFrom, CancellationToken ct);
}
