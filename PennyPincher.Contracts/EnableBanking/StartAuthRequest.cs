namespace PennyPincher.Contracts.EnableBanking;

public record StartAuthRequest(
        string AspspName,
        string AspspCountry
    );
