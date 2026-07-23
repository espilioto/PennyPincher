namespace PennyPincher.Contracts.EnableBanking;

public record AccountTransactionDto(
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
        decimal? BalanceAfterTransaction
    );
