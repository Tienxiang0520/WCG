using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using WcgWeb.Models;
using WcgWeb.Models.Battle;

namespace WcgWeb.Services;

public partial class GameEngine
{
    internal bool TrainingMode { get; set; }
    public static GameEngine CreateTraining(CardDatabase cards) => new(cards){TrainingMode=true};
    private record TrainingScene(int Version, PlayerState Player, PlayerState Computer, string Active, int Turn, TurnPhase Phase, int Seed);
    private sealed class TrainingCardConverter(CardDatabase cards) : JsonConverter<CardDefinition>
    {
        public override CardDefinition Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            reader.TokenType==JsonTokenType.String ? cards.GetCard(reader.GetString() ?? "") ?? throw new JsonException("場面包含不存在的卡片。") : throw new JsonException("卡片編號格式不合法。");
        public override void Write(Utf8JsonWriter writer, CardDefinition value, JsonSerializerOptions options) => writer.WriteStringValue(value.Id);
    }
    private JsonSerializerOptions TrainingJson()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if(info.Type != typeof(MonsterInstance)) return;
            info.Properties.Remove(info.Properties.Single(p=>p.Name==nameof(MonsterInstance.Power)));
            info.Properties.Single(p=>p.Name==nameof(MonsterInstance.CurrentPP)).Get = obj=>((MonsterInstance)obj).BasePP;
        });
        var options=new JsonSerializerOptions { IgnoreReadOnlyProperties=true, TypeInfoResolver=resolver };
        options.Converters.Add(new TrainingCardConverter(_cardDb)); return options;
    }
    public string ExportTrainingScene() => ReadConsistent(() =>
    {
        if(!TrainingMode || CurrentPhase==TurnPhase.NotStarted || IsWaiting || _effects.Count>0)
            throw new InvalidOperationException("請先完成效果選擇，再保存測試場面。");
        if(!ValidTrainingSide(Player,"player") || !ValidTrainingSide(Computer,"computer"))
            throw new InvalidOperationException("目前場面超出可保存範圍，請減少測試卡片或能量後再保存。");
        return JsonSerializer.Serialize(new TrainingScene(1,Player,Computer,CurrentTurnPlayerId,TurnNumber,CurrentPhase,Random.Shared.Next()),TrainingJson());
    });
    internal bool EditTraining(string type, TrainingEdit? edit, Guid? id) => Change(() =>
    {
        if(!TrainingMode) return Fail("此功能僅限訓練場。");
        if(type=="training-restore")
        {
            if(edit?.Scene==null || edit.Scene.Length>2_000_000)return Fail("測試場面資料不合法。");
            TrainingScene? scene;
            try { scene=JsonSerializer.Deserialize<TrainingScene>(edit?.Scene ?? "",TrainingJson()); }
            catch(Exception ex) when(ex is JsonException or NotSupportedException or ArgumentException) { return Fail("測試場面無法讀取，現有對局保持原狀。"); }
            if(scene==null || scene.Version!=1 || scene.Active is not ("player" or "computer") || scene.Turn<1 || scene.Phase is not (TurnPhase.MainPhase or TurnPhase.GameOver)
                || !ValidTrainingSide(scene.Player,"player") || !ValidTrainingSide(scene.Computer,"computer")
                || scene.Phase==TurnPhase.MainPhase && (scene.Player.HasLost || scene.Computer.HasLost || scene.Player.Hp==0 || scene.Computer.Hp==0)
                || scene.Phase==TurnPhase.GameOver && !scene.Player.HasLost && !scene.Computer.HasLost) return Fail("測試場面資料不合法。");
            ClearMatch(); Player=scene.Player; Computer=scene.Computer; Computer.IsAi=true; Player.IsAi=false;
            CurrentTurnPlayerId=scene.Active; TurnNumber=scene.Turn; CurrentPhase=scene.Phase; _random=new Random(scene.Seed);
            Log("已還原測試場面。","action"); return true;
        }
        if(CurrentPhase==TurnPhase.NotStarted || IsOver || IsWaiting || _effects.Count>0) return Fail("請等待對局與效果結算完成；結束的對局可還原測試場面。");
        if(edit?.Side is not ("player" or "computer")) return Fail("請選擇合法陣營。");
        var side=edit.Side=="player"?Player:Computer;
        if(type=="training-resources")
        {
            if(edit.Hp<0 || edit.Hp>7 || edit.TotalEnergy<0 || edit.TotalEnergy>30 || edit.AvailableEnergy<0 || edit.AvailableEnergy>edit.TotalEnergy) return Fail("生命範圍0–7；能量範圍0–30，可用能量不能超過總量。");
            side.Hp=edit.Hp; side.EnergyZone.Clear(); side.HasFilledEnergyThisTurn=false;
            for(var i=0;i<edit.TotalEnergy;i++) side.EnergyZone.Add(new(_cardDb.GetCard("WCG-101")!) {IsTapped=i>=edit.AvailableEnergy});
            CheckLethal(side); Log($"訓練調整：{side.Name}生命{side.Hp}、可用能量{side.AvailableEnergy}。","action"); return true;
        }
        if(type is "training-ready" or "training-tap" or "training-grave")
        {
            var unit=side.Board.FirstOrDefault(m=>m.InstanceId==id); if(unit==null)return Fail("指定卡片已離場。");
            if(type=="training-ready") { unit.IsTapped=false; unit.HasAttacked=false; unit.HasSummoningSickness=false; }
            else if(type=="training-tap")unit.IsTapped=true;
            else { side.Field.Remove(unit);side.Structures.Remove(unit);ReleaseAttachments(unit);side.Graveyard.Add(new(unit.Card){InstanceId=unit.InstanceId}); }
            Log("已調整測試卡片狀態（不觸發死亡效果）。","action"); return true;
        }
        var card=_cardDb.GetCard(edit.CardId??"");if(card==null)return Fail("請選擇卡片。");
        if(type=="training-hand") { if(side.Hand.Count>=30)return Fail("測試手牌最多30張。");side.Hand.Add(new(card));return true; }
        if(type is not ("training-place" or "training-enter" or "training-set")) return Fail("不支援此訓練操作。");
        if(edit.Slot<0 || edit.Slot>4 || side.Board.Any(m=>m.Slot==edit.Slot))return Fail("請選擇空的格位。");
        if(type!="training-set" && !card.IsMonster && !card.IsEnchantment)return Fail("正面格位僅能放怪物或結界；法術可加入手牌或背面蓋牌。");
        var instance=new CardInstance(card);
        if(type=="training-enter") { plannedSlots[instance.InstanceId]=edit.Slot; if(card.IsMonster)Summon(side,instance,true);else SummonStructure(side,instance); }
        else
        {
            var unit=new MonsterInstance(card){InstanceId=instance.InstanceId,Slot=edit.Slot,IsSet=type=="training-set",IsTapped=false,HasSummoningSickness=false};
            if(unit.IsUnit)side.Field.Add(unit);else side.Structures.Add(unit);
            Present(type=="training-set"?"set":"summon",side,unit.InstanceId,card:type=="training-set"?null:card,label:"布置測試卡片");
        }
        return true;
    });
    private static bool ValidTrainingSide(PlayerState? p,string id) => p!=null && p.Id==id && p.Hp>=0 && p.Hp<=7
        && p.Deck!=null && p.Hand!=null && p.EnergyZone!=null && p.Field!=null && p.Structures!=null && p.Graveyard!=null
        && p.Hand.Count<=200 && p.Deck.Count<=200 && p.EnergyZone.Count<=200 && p.Graveyard.Count<=500
        && p.Board.Count()<=5 && p.Board.All(m=>m!=null && m.Card!=null && m.Slot>=0 && m.Slot<=4
            && m.Attachments!=null && m.Attachments.Count<=100 && m.Attachments.All(a=>a!=null && a.Card!=null && a.Card.Card!=null && a.OwnerId is "player" or "computer")
            && m.ShieldEnergies!=null && m.ShieldEnergies.Count<=30 && m.ShieldEnergies.All(c=>c!=null && c.Card!=null))
        && p.Board.Select(m=>m.Slot).Distinct().Count()==p.Board.Count()
        && p.Field.All(m=>m.IsUnit) && p.Structures.All(m=>!m.IsUnit)
        && p.Deck.Concat(p.Hand).Concat(p.EnergyZone).Concat(p.Graveyard).All(c=>c!=null && c.Card!=null);
}
