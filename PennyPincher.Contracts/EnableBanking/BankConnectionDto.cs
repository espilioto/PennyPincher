namespace PennyPincher.Contracts.EnableBanking;

public record BankConnectionDto(
        string AspspName,
        string AspspCountry,
        DateTimeOffset ValidUntil,
        IReadOnlyList<LinkedAccountDto> Accounts
    );
