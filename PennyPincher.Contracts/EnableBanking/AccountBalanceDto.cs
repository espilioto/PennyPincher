namespace PennyPincher.Contracts.EnableBanking;

public record AccountBalanceDto(
        string BalanceType,
        decimal Amount,
        string Currency,
        DateTimeOffset? LastChangeDateTime
    );
