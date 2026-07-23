namespace PennyPincher.Contracts.EnableBanking;

public record AspspDto(
        string Name,
        string Country,
        IReadOnlyList<string> PsuTypes,
        bool Beta
    );
