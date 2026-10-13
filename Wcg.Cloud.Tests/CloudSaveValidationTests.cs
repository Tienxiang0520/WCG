using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WcgCloud;

namespace WcgCloudTests;
public sealed class CloudSaveValidationTests
{
    readonly CloudSaveValidator validator=new();
    [Fact] public void LegalEmptyAndRankedSavesAreAccepted()
    {
        validator.Validate(new(new(),new()));
        validator.Validate(new(new(){{"ranked",JsonSerializer.Serialize(new {Version=2,Season="2026-10",Stars=15,BestStars=15,SeasonBest=15,Wins=0,Losses=0,History=Array.Empty<object>()})}},new(){{"wcg.confirmEnergy","false"}}));
    }
    [Fact] public void AClientCannotIncludeIdentityOrStorageCredentialsInSaveData()=>Assert.Throws<InvalidDataException>(()=>validator.Validate(new(new(){{"owner","{}"}},new())));
    [Fact] public void MalformedSectionsCannotOverwriteAValidCloudSave()=>Assert.ThrowsAny<JsonException>(()=>validator.Validate(new(new(){{"ranked","broken"}},new())));
    [Fact] public void UnknownCardsAndIllegalDecksAreRejected()=>Assert.Throws<InvalidDataException>(()=>validator.Validate(new(new(){{"decks","[{\"Id\":\"custom\",\"MainWill\":\"秩序\",\"CardIds\":[\"WCG-999\"]}]"}},new())));
    [Fact] public void UnsupportedPreferenceKeysAreRejected()=>Assert.Throws<InvalidDataException>(()=>validator.Validate(new(new(),new(){{"access_token","anything"}})));
    [Fact] public void InvalidAvatarCannotBeUploaded()=>Assert.Throws<InvalidDataException>(()=>validator.Validate(new(new(){{"profile","{\"Avatar\":\"data:image/png;base64,bad\"}"}},new())));
    [Fact] public async Task AnonymousCallsCannotReachCloudStorage()
    {
        var functions=new CloudFunctions(new CloudIdentity("api"),null!,validator);
        var context=new DefaultHttpContext();context.Request.Method="GET";
        Assert.IsType<UnauthorizedObjectResult>(await functions.Save(context.Request,default));
        Assert.IsType<UnauthorizedObjectResult>(await functions.Lease(context.Request,default));
    }
}
