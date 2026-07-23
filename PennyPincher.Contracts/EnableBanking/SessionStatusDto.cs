namespace PennyPincher.Contracts.EnableBanking;

public record SessionStatusDto(
        bool IsLinked,
        DateTimeOffset? ValidUntil,
        int AccountCount
    );
