using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using WcgCloud;

namespace WcgCloudTests;
public sealed class CloudIdentityTests : IDisposable
{
    readonly RSA rsa = RSA.Create(2048);
    const string Audience = "aed76f8d-54ad-4054-a8f3-7cb4ecb33977";
    const string Tenant = "9188040d-6c67-4c5b-b112-36a304b66dad";
    const string User = "0caf3951-f51d-481a-b694-a5d80dc0a3d8";
    readonly CloudIdentity identity;
    public CloudIdentityTests()
    {
        var config = new OpenIdConnectConfiguration();config.SigningKeys.Add(new RsaSecurityKey(rsa){KeyId="test"});
        identity = new(Audience, new StaticConfigurationManager<OpenIdConnectConfiguration>(config));
    }
    string Token(string audience=Audience,string scope="Save.ReadWrite",string user=User,string? issuer=null,bool expired=false,RSA? signing=null) =>
        "Bearer " + new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Audience=audience,Issuer=issuer??$"https://login.microsoftonline.com/{Tenant}/v2.0",
            IssuedAt=DateTime.UtcNow.AddMinutes(-10),NotBefore=DateTime.UtcNow.AddMinutes(-10),Expires=DateTime.UtcNow.AddMinutes(expired?-5:5),
            Claims=new Dictionary<string,object>{{"tid",Tenant},{"oid",user},{"scp",scope}},
            SigningCredentials=new(new RsaSecurityKey(signing??rsa){KeyId="test"},SecurityAlgorithms.RsaSha256)
        });
    [Fact] public async Task VerifiedMicrosoftObjectIdentityOwnsOnlyItsOwnSave()
    {
        var first=await identity.Owner(Token());Assert.NotNull(first);Assert.Equal(64,first.Length);
        Assert.Equal(first,await identity.Owner(Token()));Assert.NotEqual(first,await identity.Owner(Token(user:Guid.NewGuid().ToString())));
    }
    [Fact] public async Task WrongApiAudienceCannotUseTheStorageApi()=>Assert.Null(await identity.Owner(Token(audience:Guid.NewGuid().ToString())));
    [Fact] public async Task LoginWithoutTheSavePermissionIsRejected()=>Assert.Null(await identity.Owner(Token(scope:"openid profile")));
    [Fact] public async Task ForgedIssuerIsRejectedEvenWithAValidSignature()=>Assert.Null(await identity.Owner(Token(issuer:"https://attacker.example/v2.0")));
    [Fact] public async Task ExpiredTokenIsRejected()=>Assert.Null(await identity.Owner(Token(expired:true)));
    [Fact] public async Task ForgedSigningKeyIsRejected(){using var fake=RSA.Create(2048);Assert.Null(await identity.Owner(Token(signing:fake)));}
    [Fact] public async Task UserIdentityCannotBeAnEmailOrMissing(){Assert.Null(await identity.Owner(Token(user:"person@example.com")));Assert.Null(await identity.Owner(null));}
    public void Dispose()=>rsa.Dispose();
}
