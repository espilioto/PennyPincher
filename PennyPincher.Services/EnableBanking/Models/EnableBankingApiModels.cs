using System.Text.Json.Serialization;

namespace PennyPincher.Services.EnableBanking.Models;

// Internal wire-format DTOs matching Enable Banking REST API.
// Not public — callers see PennyPincher.Contracts.EnableBanking.* instead.

internal record AuthStartBody(
    [property: JsonPropertyName("access")] AccessSpec Access,
    [property: JsonPropertyName("aspsp")] AspspSpec Aspsp,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("redirect_url")] string RedirectUrl,
    [property: JsonPropertyName("psu_type")] string PsuType
);

internal record AccessSpec(
    [property: JsonPropertyName("valid_until")] string ValidUntil
);

internal record AspspSpec(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("country")] string Country
);

internal record AuthStartResponse(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("authorization_id")] string? AuthorizationId
);

internal record SessionsBody(
    [property: JsonPropertyName("code")] string Code
);

internal record SessionsResponse(
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("access")] SessionAccess? Access,
    [property: JsonPropertyName("accounts")] List<EbAccount> Accounts
);

internal record SessionAccess(
    [property: JsonPropertyName("valid_until")] DateTimeOffset? ValidUntil
);

internal record EbAccount(
    [property: JsonPropertyName("uid")] string Uid,
    [property: JsonPropertyName("account_id")] EbAccountId? AccountId,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("product")] string? Product,
    [property: JsonPropertyName("currency")] string? Currency,
    [property: JsonPropertyName("cash_account_type")] string? CashAccountType
);

internal record EbAccountId(
    [property: JsonPropertyName("iban")] string? Iban,
    [property: JsonPropertyName("other")] EbOtherId? Other
);

internal record EbOtherId(
    [property: JsonPropertyName("identification")] string? Identification
);

internal record BalancesResponse(
    [property: JsonPropertyName("balances")] List<EbBalance> Balances
);

internal record EbBalance(
    [property: JsonPropertyName("balance_type")] string BalanceType,
    [property: JsonPropertyName("balance_amount")] EbAmount BalanceAmount,
    [property: JsonPropertyName("last_change_date_time")] DateTimeOffset? LastChangeDateTime
);

internal record EbAmount(
    [property: JsonPropertyName("amount")] string Amount,
    [property: JsonPropertyName("currency")] string Currency
);

internal record TransactionsResponse(
    [property: JsonPropertyName("transactions")] List<EbTransaction> Transactions,
    [property: JsonPropertyName("continuation_key")] string? ContinuationKey
);

internal record EbTransaction(
    [property: JsonPropertyName("entry_reference")] string? EntryReference,
    [property: JsonPropertyName("transaction_amount")] EbAmount TransactionAmount,
    [property: JsonPropertyName("credit_debit_indicator")] string CreditDebitIndicator,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("booking_date")] DateOnly? BookingDate,
    [property: JsonPropertyName("transaction_date")] DateOnly? TransactionDate,
    [property: JsonPropertyName("value_date")] DateOnly? ValueDate,
    [property: JsonPropertyName("remittance_information")] List<string>? RemittanceInformation,
    [property: JsonPropertyName("creditor")] EbParty? Creditor,
    [property: JsonPropertyName("debtor")] EbParty? Debtor,
    [property: JsonPropertyName("merchant_category_code")] string? MerchantCategoryCode,
    [property: JsonPropertyName("balance_after_transaction")] EbAmount? BalanceAfterTransaction
);

internal record EbParty(
    [property: JsonPropertyName("name")] string? Name
);
