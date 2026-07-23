namespace PennyPincher.Services.EnableBanking;

public class EnableBankingOptions
{
    public const string SectionName = "EnableBanking";

    public string BaseUrl { get; set; } = string.Empty;
    public string ApplicationId { get; set; } = string.Empty;

    // Supply the RSA private key either as an inline PEM string (PrivateKeyPem,
    // typical for prod env vars) or as a filesystem path (PrivateKeyPemPath,
    // typical for dev). Path wins if both are set.
    public string PrivateKeyPem { get; set; } = string.Empty;
    public string PrivateKeyPemPath { get; set; } = string.Empty;

    public string RedirectUri { get; set; } = string.Empty;
    public int JwtTtlSeconds { get; set; } = 3600;
}
