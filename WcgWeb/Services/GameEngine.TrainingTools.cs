using System.Text.Json.Nodes;
using WcgWeb.Models;
using WcgWeb.Models.Battle;

namespace WcgWeb.Services;

// 訓練場 extra tools: counters, deck top / draw, turn controls, swapping sides (to play the opponent's turn with the
// normal controls) and one-click preset scenes. Only TrainingMode engines reach this; ranked and normal play reject it.
public partial class GameEngine
{
    internal static readonly string[] TrainingPresets = ["taunt", "combat", "countdown", "reveal", "untap", "defeat", "victory"];
    private const int TrainingCounterMax = 20;

    // Returns null when the type is not one of these tools (the caller handles it).
    private bool? TrainingTool(string type, TrainingEdit? edit, Guid? id)
    {
        if (type is not ("training-counters" or "training-top" or "training-draw" or "training-endturn" or "training-turn" or "training-swap")) return null;
        if (CurrentPhase == TurnPhase.NotStarted || IsOver || IsWaiting || _effects.Count > 0) return Fail("請等待對局與效果結算完成；結束的對局可還原測試場面。");
        if (type == "training-endturn")
        {
            if (!EndTurn()) return Fail("目前無法結束回合。");
            Log("訓練調整：直接結束目前回合。", "action"); return true;
        }
        if (type == "training-swap") return SwapTrainingSides();
        if (edit?.Side is not ("player" or "computer")) return Fail("請選擇合法陣營。");
        var side = edit.Side == "player" ? Player : Computer;
        if (type == "training-turn")
        {
            if (edit.Turn < 1 || edit.Turn > 999) return Fail("回合數範圍 1–999。");
            TurnNumber = edit.Turn; Log($"訓練調整：回合數改為 {TurnNumber}。", "action"); return true;
        }
        if (type == "training-draw")
        {
            if (side.Deck.Count == 0) return Fail("牌庫已空。");
            DrawMany(side, 1); return true;
        }
        if (type == "training-top")
        {
            var card = _cardDb.GetCard(edit.CardId ?? ""); if (card == null) return Fail("請選擇卡片。");
            if (side.Deck.Count >= 200) return Fail("測試牌庫最多200張。");
            // Prefer moving a copy already in the deck so the deck size stays the same.
            var existing = side.Deck.FirstOrDefault(c => c.Card.Id == card.Id);
            if (existing != null) side.Deck.Remove(existing);
            side.Deck.Insert(0, existing ?? new CardInstance(card));
            Log($"訓練調整：{side.Name}牌庫頂放置【{card.Name}】。", "action"); return true;
        }
        var unit = side.Board.FirstOrDefault(m => m.InstanceId == id); if (unit == null) return Fail("指定卡片已離場。");
        int[] values = [edit.Countdown, edit.Dp, edit.Shield, edit.Corrosion, edit.Blood, edit.Spring];
        if (values.Any(v => v < -1 || v > TrainingCounterMax)) return Fail($"指示物數量範圍 0–{TrainingCounterMax}。");
        if (edit.Countdown >= 0) unit.Countdown = edit.Countdown;
        if (edit.Dp >= 0) unit.DpCounters = edit.Dp;
        if (edit.Shield >= 0) unit.ShieldCounters = edit.Shield;
        if (edit.Corrosion >= 0) unit.CorrosionCounters = edit.Corrosion;
        if (edit.Blood >= 0) unit.BloodCounters = edit.Blood;
        if (edit.Spring >= 0) unit.SpringCounters = edit.Spring;
        RefreshBoard();
        Present("status", side, unit.InstanceId, card: unit.Card, label: "設定測試指示物");
        Log($"訓練調整：【{unit.Card.Name}】倒數 {unit.Countdown}、DP {unit.DpCounters}、聖盾 {unit.ShieldCounters}、腐蝕 {unit.CorrosionCounters}、血誓 {unit.BloodCounters}、聖泉 {unit.SpringCounters}。", "action");
        return true;
    }

    // Swap the two sides so the user controls the opponent's cards (and turn) with the normal controls. Every
    // "player"/"computer" reference in the scene is swapped too; the engine keeps one human side by design.
    private bool SwapTrainingSides()
    {
        var node = JsonNode.Parse(ExportTrainingScene())!.AsObject();
        var player = node["Player"]!; var computer = node["Computer"]!;
        node.Remove("Player"); node.Remove("Computer");
        node["Player"] = computer; node["Computer"] = player;
        SwapIds(node);
        player["Name"] = "電腦"; computer["Name"] = "玩家";
        var turn = TurnNumber;
        if (!RestoreTrainingScene(node.ToJsonString())) return false;
        TurnNumber = turn; Log("訓練調整：已互換雙方，現在操控原本的對手。", "action"); return true;
    }
    private static void SwapIds(JsonNode? n)
    {
        if (n is JsonObject o)
            foreach (var key in o.Select(p => p.Key).ToArray())
            {
                if (key is "Id" or "OwnerId" or "ShieldOwnerId" or "SilenceOwnerId" or "Active" && o[key] is JsonValue v && v.TryGetValue<string>(out var s) && s is "player" or "computer")
                    o[key] = s == "player" ? "computer" : "player";
                else SwapIds(o[key]);
            }
        else if (n is JsonArray a) foreach (var x in a) SwapIds(x);
    }

    // Built-in scenes made of real cards, for the manual checks that are hard to reach in a normal match.
    private bool TrainingPreset(string? name)
    {
        if (!TrainingPresets.Contains(name)) return Fail("找不到這個預設情境。");
        ClearMatch();
        foreach (var p in new[] { Player, Computer })
        {
            for (var i = 0; i < 20; i++) p.Deck.Add(Inst("WCG-101"));
            for (var i = 0; i < 3; i++) p.Hand.Add(Inst("WCG-101"));
            for (var i = 0; i < 4; i++) p.EnergyZone.Add(Inst("WCG-101"));
            p.Hp = 7;
        }
        var active = "player";
        switch (name)
        {
            case "taunt": // 白銀巡邏衛 (嘲諷) guards 巡邏輕步兵; attack the face or the other monster and taunt blocks it.
                Unit(Player, "WCG-055", 1); Unit(Player, "WCG-154", 2);
                Unit(Computer, "WCG-065", 2); Unit(Computer, "WCG-101", 3);
                break;
            case "combat": // 1900 vs 1000 (defender breaks), 1300 vs 1300 (both break).
                Unit(Player, "WCG-055", 1); Unit(Player, "WCG-154", 3);
                Unit(Computer, "WCG-101", 1); Unit(Computer, "WCG-154", 3);
                break;
            case "countdown": // End the computer's turn: at your turn start 星沙學徒 fires (draw 2), 封時守衛 2→1, 倒懸時鐘 fires.
                Unit(Player, "WCG-409", 0).Countdown = 1; Unit(Player, "WCG-413", 2).Countdown = 2; Unit(Player, "WCG-410", 4).Countdown = 1;
                Unit(Computer, "WCG-101", 1); Unit(Computer, "WCG-154", 3);
                active = "computer";
                break;
            case "reveal": // Play 擲骨巫童: the deck top 雪人巨獸 is revealed (a monster, so draw 1).
                Player.Hand.Add(Inst("WCG-418")); Player.Hand.Add(Inst("WCG-424"));
                Player.Deck.Insert(0, Inst("WCG-114")); Player.Deck.Insert(1, Inst("WCG-055"));
                for (var i = 0; i < 4; i++) Player.EnergyZone.Add(Inst("WCG-101"));
                break;
            case "untap": // Your monsters are tapped during the computer's turn; end it and they untap. A summon enters tapped.
                Unit(Player, "WCG-055", 1).IsTapped = true; Unit(Player, "WCG-154", 2).IsTapped = true;
                Unit(Computer, "WCG-101", 2);
                active = "computer";
                break;
            case "defeat": // You have 1 life and no blockers; step or resume the computer and it attacks for the win.
                Player.Hp = 1; Unit(Computer, "WCG-055", 1); Unit(Computer, "WCG-154", 3);
                active = "computer";
                break;
            case "victory": // The computer has 1 life and no monsters; attack its avatar.
                Computer.Hp = 1; Unit(Player, "WCG-055", 1);
                break;
        }
        CurrentTurnPlayerId = active; TurnNumber = 5; CurrentPhase = TurnPhase.MainPhase;
        RefreshBoard();
        Present("turn", ActivePlayer, label: "訓練情境");
        Log("已載入訓練情境。", "action");
        return true;
    }
    private CardInstance Inst(string id) => new(_cardDb.GetCard(id) ?? throw new InvalidOperationException($"訓練情境缺少卡片 {id}。"));
    private MonsterInstance Unit(PlayerState p, string id, int slot)
    {
        var m = new MonsterInstance(Inst(id).Card) { Slot = slot, IsTapped = false, HasSummoningSickness = false };
        if (m.IsUnit) p.Field.Add(m); else p.Structures.Add(m);
        return m;
    }
}
