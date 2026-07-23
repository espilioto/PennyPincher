namespace PennyPincher.Contracts.EnableBanking;

// One row per linked bank: the summed balance across that bank's accounts.
// Available is false when the balance couldn't be fetched (e.g. bank disruption
// or expired session), in which case Total/Currency are null.
public record BankBalanceDto(
        string AspspName,
        string AspspCountry,
        decimal? Total,
        string? Currency,
        bool Available
    );
