namespace PennyPincher.Contracts.EnableBanking;

public record CompleteAuthResponse(
        string SessionId,
        DateTimeOffset ValidUntil,
        IReadOnlyList<LinkedAccountDto> Accounts
    );
