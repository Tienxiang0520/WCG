using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WcgCloud;
using WcgWeb.Models;
using WcgWeb.Models.Battle;
using WcgWeb.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace WcgCloudTests;
public sealed class CloudSaveValidationTests
{
    readonly CloudSaveValidator validator=new();
    private sealed class MemoryStorage : IPlayerStorage
    {
        public object Gate { get; }=new();
        public Dictionary<string,string> Values { get; }=new();
        public string? Read(string key)=>Values.GetValueOrDefault(key);
        public void Write(string key,string value,string? expected)=>Values[key]=value;
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void ActiveJournalsAcrossCardBalanceVersionsCanBeSynced(int era)
    {
        string Read(string name)
        {
            using var stream=typeof(CloudSaveValidator).Assembly.GetManifestResourceStream(name)!;
            using var reader=new StreamReader(stream);return reader.ReadToEnd();
        }
        var definitions=JsonSerializer.Deserialize<List<CardDefinition>>(Read("WcgCloud.Cards"))!;
        if(era>=1)
        {
            /* 舊時代的對局是用規則簡化前的卡面建立的。 */
            definitions=definitions.Where(c=>int.Parse(c.Id[4..])<205).ToList();
            /* 2026-10-17 法師直接發動之前的 029、035 卡文。 */
            var mageDirect=JsonSerializer.Deserialize<List<CardDefinition>>(Read("WcgWeb.Balance.BeforeMageDirect"))!.ToDictionary(c=>c.Id);
            definitions=definitions.Select(c=>mageDirect.GetValueOrDefault(c.Id,c)).ToList();
            /* 2026-10-17 聖盾 PP 還原之前的 PP。 */
            var shieldPp=JsonSerializer.Deserialize<List<CardDefinition>>(Read("WcgWeb.Balance.BeforeShieldPp"))!.ToDictionary(c=>c.Id);
            definitions=definitions.Select(c=>shieldPp.GetValueOrDefault(c.Id,c)).ToList();
            /* 2026-10-17 四張卡費用 +1 之前的費用與 PP。 */
            var costUp=JsonSerializer.Deserialize<List<CardDefinition>>(Read("WcgWeb.Balance.BeforeCostUp"))!.ToDictionary(c=>c.Id);
            definitions=definitions.Select(c=>costUp.GetValueOrDefault(c.Id,c)).ToList();
            /* 2026-10-17 聖盾指示物之前的卡文。 */
            var shield=JsonSerializer.Deserialize<List<CardDefinition>>(Read("WcgWeb.Balance.BeforeShieldCounter"))!.ToDictionary(c=>c.Id);
            definitions=definitions.Select(c=>shield.GetValueOrDefault(c.Id,c)).ToList();
            /* 2026-10-17 理智加強之前的數值與卡文。 */
            var reason=JsonSerializer.Deserialize<List<CardDefinition>>(Read("WcgWeb.Balance.BeforeReasonBuff"))!.ToDictionary(c=>c.Id);
            definitions=definitions.Select(c=>reason.GetValueOrDefault(c.Id,c)).ToList();
            /* 這些時代的卡在 2026-10-16 改成雙色之前都是單色。 */
            var single=JsonSerializer.Deserialize<List<CardDefinition>>(Read("WcgWeb.Balance.BeforeDualWill"))!.ToDictionary(c=>c.Id);
            definitions=definitions.Select(c=>single.GetValueOrDefault(c.Id,c)).ToList();
            var deflation=JsonSerializer.Deserialize<List<CardDefinition>>(Read("WcgWeb.Balance.BeforeDeflation"))!.ToDictionary(c=>c.Id);
            definitions=definitions.Select(c=>deflation.GetValueOrDefault(c.Id,c)).ToList();
            var prior=JsonSerializer.Deserialize<List<CardDefinition>>(Read("WcgWeb.Balance.BeforeRulesSimplification"))!.ToDictionary(c=>c.Id);
            definitions=definitions.Select(c=>prior.GetValueOrDefault(c.Id,c)).ToList();
        }
        if(era>=1) definitions=definitions.Where(c=>c.Id is not ("WCG-201" or "WCG-202" or "WCG-203" or "WCG-204")).ToList();
        if(era>=2) definitions=definitions.Where(c=>c.Id!="WCG-200").ToList();
        if(era>=3)
        {
            var prior=JsonSerializer.Deserialize<List<CardDefinition>>(Read("WcgWeb.Balance.BeforeStrongBuildAdjustment"))!.ToDictionary(c=>c.Id);
            definitions=definitions.Select(c=>prior.GetValueOrDefault(c.Id,c)).ToList();
        }
        if(era>=4)
        {
            var prior=JsonSerializer.Deserialize<List<CardDefinition>>(Read("WcgWeb.Balance.BeforeOctoberBalance"))!.ToDictionary(c=>c.Id);
            definitions=definitions.Select(c=>prior.GetValueOrDefault(c.Id,c)).ToList();
        }
        var presets=Read("WcgCloud.Presets");
        /* 舊時代的 v0.6 深淵預組還沒有 PR E 換入的新卡。 */
        if(era>=1) presets=presets.Replace("\"WCG-249\"","\"WCG-128\"").Replace("\"WCG-250\"","\"WCG-169\"").Replace("\"WCG-212\"","\"WCG-169\"");
        var cards=new CardDatabase(JsonSerializer.Serialize(definitions),presets);
        var opponents=new RankedDecks(cards,Read("WcgCloud.RankedDecks"));
        var storage=new MemoryStorage();var store=new RankedStore(storage);
        var session=new RankedSession(cards,new DeckService(cards,storage),store,opponents,NullLogger<BattleCoordinator>.Instance);
        try
        {
            Assert.True(session.Start(cards.PresetDecks[4]).Success);
            var profile=session.Read();profile.Match!.PlayerFirst=true;store.Save(profile);
            session.Coordinator.Dispose();
            session=new RankedSession(cards,new DeckService(cards,storage),store,opponents,NullLogger<BattleCoordinator>.Instance);
            var state=session.Bridge.Snapshot();
            Assert.True(session.Bridge.Submit(new BattleCommand(Guid.NewGuid(),state.MatchId,state.Revision,"energy",session.Engine.Player.Hand[0].InstanceId)).Success);
            validator.Validate(new(new(){{"ranked",JsonSerializer.Serialize(session.Read())}},new()));
        }
        finally {session.Coordinator.Dispose();}
    }
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
