namespace PennyPincher.Contracts.EnableBanking;

public record StartAuthResponse(
        string AuthUrl,
        string State
    );
