namespace PennyPincher.Contracts.EnableBanking;

public record LinkedAccountDto(
        string Uid,
        string? Iban,
        string? Name,
        string? Product,
        string? Currency,
        string? CashAccountType
    );
