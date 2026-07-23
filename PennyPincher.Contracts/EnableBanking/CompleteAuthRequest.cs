namespace PennyPincher.Contracts.EnableBanking;

public record CompleteAuthRequest(
        string Code,
        string? State = null
    );
