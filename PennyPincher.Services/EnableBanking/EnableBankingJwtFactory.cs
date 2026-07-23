using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;

namespace PennyPincher.Services.EnableBanking;

public interface IEnableBankingJwtFactory
{
    string Create();
}

// Mints an Enable Banking auth JWT per https://enablebanking.com/docs/api/reference/#authentication
// Header: typ=JWT, alg=RS256, kid=<ApplicationId>
// Claims: iss=enablebanking.com, aud=api.enablebanking.com, iat, exp
// Signed with the RSA private key whose public cert was uploaded to the Control Panel.
public class EnableBankingJwtFactory : IEnableBankingJwtFactory
{
    private readonly EnableBankingOptions _options;

    public EnableBankingJwtFactory(IOptions<EnableBankingOptions> options)
    {
        _options = options.Value;
    }

    public string Create()
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(LoadPem());

        var now = DateTimeOffset.UtcNow;
        var exp = now.AddSeconds(_options.JwtTtlSeconds);

        var signingCredentials = new SigningCredentials(
            new RsaSecurityKey(rsa.ExportParameters(true)),
            SecurityAlgorithms.RsaSha256);

        var header = new JwtHeader(signingCredentials)
        {
            ["kid"] = _options.ApplicationId,
            ["typ"] = "JWT"
        };

        var payload = new JwtPayload
        {
            ["iss"] = "enablebanking.com",
            ["aud"] = "api.enablebanking.com",
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = exp.ToUnixTimeSeconds()
        };

        var token = new JwtSecurityToken(header, payload);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private string LoadPem()
    {
        if (!string.IsNullOrWhiteSpace(_options.PrivateKeyPemPath))
            return File.ReadAllText(_options.PrivateKeyPemPath);
        if (!string.IsNullOrWhiteSpace(_options.PrivateKeyPem))
            return _options.PrivateKeyPem;
        throw new InvalidOperationException(
            "EnableBanking private key missing — set EnableBanking:PrivateKeyPemPath or EnableBanking:PrivateKeyPem in configuration.");
    }
}
