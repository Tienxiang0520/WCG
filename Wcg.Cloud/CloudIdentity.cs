using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace WcgCloud;

public sealed class CloudIdentity
{
    private readonly string audience;
    private readonly IConfigurationManager<OpenIdConnectConfiguration> metadata;
    public CloudIdentity(string audience, IConfigurationManager<OpenIdConnectConfiguration>? metadata = null)
    {
        this.audience = audience;
        this.metadata = metadata ?? new ConfigurationManager<OpenIdConnectConfiguration>("https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration", new OpenIdConnectConfigurationRetriever());
    }
    public async Task<string?> Owner(string? authorization, CancellationToken cancellation = default)
    {
        if (authorization is null || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || authorization.Length > 20000) return null;
        var configuration = await metadata.GetConfigurationAsync(cancellation);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(authorization[7..], new TokenValidationParameters
        {
            ValidateAudience = true, ValidAudiences = [audience, "api://" + audience],
            ValidateLifetime = true, ClockSkew = TimeSpan.FromMinutes(1),
            RequireSignedTokens = true, ValidateIssuerSigningKey = true, IssuerSigningKeys = configuration.SigningKeys,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidateIssuer = true,
            IssuerValidator = (issuer, token, _) =>
            {
                var jwt = (JsonWebToken)token;
                if (!jwt.TryGetClaim("tid", out var tenant) || !Guid.TryParse(tenant.Value, out var id) || issuer != $"https://login.microsoftonline.com/{id:D}/v2.0")
                    throw new SecurityTokenInvalidIssuerException();
                return issuer;
            }
        });
        if (!result.IsValid)
        {
            if (result.Exception is SecurityTokenSignatureKeyNotFoundException) metadata.RequestRefresh();
            return null;
        }
        var validated = (JsonWebToken)result.SecurityToken;
        if (!validated.TryGetClaim("scp", out var scope) || !scope.Value.Split(' ').Contains("Save.ReadWrite") ||
            !validated.TryGetClaim("oid", out var user) || !Guid.TryParse(user.Value, out var userId) ||
            !validated.TryGetClaim("tid", out var tid) || !Guid.TryParse(tid.Value, out var tenantId)) return null;
        // Stable Microsoft object identity, never an email or a client-supplied player id.
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId:D}:{userId:D}")));
    }
}
