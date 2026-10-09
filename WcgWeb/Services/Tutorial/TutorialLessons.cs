using WcgWeb.Models;
using static WcgWeb.Services.Tutorial.TutorialAllow;

namespace WcgWeb.Services.Tutorial;

// Every lesson of the 教學 section. Battle lessons start from a fixed board and accept only the actions of the current step;
// the rules themselves are the normal v0.6 engine. Tours walk through a real page.
public static class TutorialLessons
{
    public const string BeginnerId = "basics";
    private static readonly string[] Fillers = ["WCG-101", "WCG-154", "WCG-101", "WCG-154", "WCG-101", "WCG-154", "WCG-101", "WCG-154"];

    private static bool OnField(PlayerState p, string id) => p.Field.Any(m => m.Card.Id == id);
    private static bool OnBoard(PlayerState p, string id) => p.Board.Any(m => m.Card.Id == id);
    private static MonsterInstance? Unit(PlayerState p, string id) => p.Field.FirstOrDefault(m => m.Card.Id == id);
    private static bool Settled(GameEngine e) => !e.IsWaiting;
    private static string[] Deck(params string[] top) => [.. top, .. Fillers];

    public static readonly IReadOnlyList<TutorialLesson> All =
    [
        new()
        {
            Id = BeginnerId, Beginner = true, Icon = "flag", Title = "新手教學：第一場對決",
            Summary = "從填能量、召喚、結束回合到攻擊玩家，用一場小對局學會魂誓的基本流程。",
            Topics = ["對局目標", "填能量", "費用與出牌", "五個格位", "召喚橫置", "回合流程與抽牌", "攻擊玩家", "衝鋒", "法術", "勝利條件"],
            Scene = new(
                new() { Hand = ["WCG-121", "WCG-101", "WCG-014", "WCG-154"], Deck = Deck("WCG-005"), Energy = 1 },
                new() { Hp = 3, Hand = ["WCG-154", "WCG-101"], Deck = Deck(), Energy = 1 }),
            Steps =
            [
                new() { Title = "歡迎來到魂誓", Spot = ["face", "hero"],
                    Text = "這是一場固定場面的教學對局。上方是電腦，下方是你。雙方各有 7 點生命，先讓對手生命歸零就獲勝。正式對局以 50 張牌組開始，起手 7 張，先攻玩家的第一回合不抽牌。" },
                new() { Title = "手牌與卡面", Spot = ["hand"],
                    Text = "下方是你的手牌。卡面左上角是費用；怪物另有 PP（戰力，交戰時比大小）與 DP（攻擊玩家時造成的傷害）。點一下卡片可以放大查看完整效果。" },
                new() { Title = "填能量", Spot = ["hand:WCG-121", "energy"], Allow = [Energy("WCG-121")],
                    Done = e => e.Player.EnergyZone.Count == 2,
                    Text = "每回合可以把任意 1 張手牌背面放進能量區，成為 1 點能量（不會抽牌）。請把「冒險者行囊」拖到左下角亮起的能量區並確認。",
                    Hint = "這一步請把「冒險者行囊」拖到能量區。" },
                new() { Title = "召喚怪物", Spot = ["hand:WCG-101", "own:2"], Allow = [Play("WCG-101"), Slot()],
                    Done = e => OnField(e.Player, "WCG-101"),
                    Text = "出牌時要橫置與費用相同數量的能量。把「巡邏輕步兵」（費用 1）拖到己方任一空格。場上 5 個格位由怪物、結界與蓋牌共用，放下後不能換位。",
                    Hint = "請把「巡邏輕步兵」拖到己方空格。" },
                new() { Title = "剛進場會橫置", Spot = ["own-card:WCG-101"],
                    Text = "剛召喚的怪物會橫置，這回合不能攻擊。只有具「衝鋒」的怪物以直立狀態進場，可以立刻攻擊。" },
                new() { Title = "結束回合", Spot = ["end"], Allow = [End()],
                    Done = e => e.CurrentTurnPlayerId == "computer" || e.TurnNumber > 3,
                    Text = "這回合能做的事都做完了。按「結束回合」，換電腦行動。",
                    Hint = "請按「結束回合」。" },
                new() { Title = "電腦的回合", Spot = ["face"], Done = e => e.CurrentTurnPlayerId == "player" && e.TurnNumber >= 5 && Settled(e),
                    Text = "電腦正在行動，請稍候……" },
                new() { Title = "新的回合開始", Spot = ["energy", "hand:WCG-005"],
                    Text = "回合開始時，你的能量與怪物全部轉回直立，並從牌庫抽 1 張牌。你抽到了「突擊狼騎兵」。牌庫抽空時若需要抽牌，就會直接落敗。" },
                new() { Title = "攻擊玩家", Spot = ["own-card:WCG-101", "face"], Allow = [Attack("WCG-101")],
                    Done = e => e.Computer.Hp == 2,
                    Text = "把直立的「巡邏輕步兵」拖到上方的電腦頭像，直接攻擊玩家，造成等同 DP 的傷害。攻擊後怪物會橫置。",
                    Hint = "請把「巡邏輕步兵」拖到電腦頭像。" },
                new() { Title = "再填一次能量", Spot = ["hand:WCG-154", "energy"], Allow = [Energy("WCG-154")],
                    Done = e => e.Player.EnergyZone.Count == 3,
                    Text = "每回合都能填 1 次能量。把「淡水狂鱷」拖到能量區，讓你有 3 點能量。",
                    Hint = "這一步請把「淡水狂鱷」拖到能量區。" },
                new() { Title = "衝鋒", Spot = ["hand:WCG-005"], Allow = [Play("WCG-005"), Slot()],
                    Done = e => OnField(e.Player, "WCG-005"),
                    Text = "「突擊狼騎兵」具有衝鋒：進場時直立，這回合就能攻擊。把它拖到任一空格。",
                    Hint = "請把「突擊狼騎兵」拖到己方空格。" },
                new() { Title = "衝鋒攻擊", Spot = ["own-card:WCG-005", "face"], Allow = [Attack("WCG-005")],
                    Done = e => e.Computer.Hp == 1,
                    Text = "立刻用「突擊狼騎兵」攻擊電腦頭像。",
                    Hint = "請把「突擊狼騎兵」拖到電腦頭像。" },
                new() { Title = "用法術收尾", Spot = ["hand:WCG-014", "face"], Allow = [Play("WCG-014")],
                    Done = e => e.Computer.HasLost,
                    Text = "法術打出後立即結算，然後進入墓地。把「熾紅戰斧」拖到電腦頭像（或戰場中央），造成 1 點傷害，完成致命一擊。",
                    Hint = "請打出「熾紅戰斧」。" },
                new() { Title = "勝利！",
                    Text = "電腦生命歸零，你贏了！勝利條件有兩種：對手生命歸零，或對手需要抽牌時牌庫已空。對局中也可以按「投降」認輸。接下來的課程會帶你認識交戰、嘲諷、聖盾與各種關鍵字。" }
            ]
        },
        new()
        {
            Id = "combat", Icon = "swords", Title = "怪物交戰：PP 比大小",
            Summary = "攻擊敵方怪物時比較 PP，高者存活、平手同歸於盡；學會預覽勝負與查看墓地。",
            Topics = ["怪物攻擊怪物", "PP 比較", "平手同歸於盡", "無累積傷害", "攻擊後橫置", "墓地與紀錄", "略過怪物直接攻擊玩家"],
            Scene = new(
                new() { Hand = ["WCG-101"], Deck = Deck(), Energy = 2, Board = [new("WCG-114", 1), new("WCG-154", 2), new("WCG-155", 3)] },
                new() { Deck = Deck(), Energy = 2, Board = [new("WCG-157", 1), new("WCG-156", 2), new("WCG-101", 3)] }),
            Steps =
            [
                new() { Title = "交戰規則", Spot = ["field"],
                    Text = "怪物攻擊怪物時比較目前 PP：較高者存活、較低者被消滅；PP 相同則同歸於盡。傷害不會累積，存活的怪物不會留下傷痕。DP 只在攻擊玩家時使用。" },
                new() { Title = "以強擊弱", Spot = ["own-card:WCG-114", "enemy-card:WCG-101"], Allow = [Attack("WCG-114", "WCG-101")],
                    Done = e => !OnField(e.Computer, "WCG-101"),
                    Text = "把「雪人巨獸」（2200）拖到敵方「巡邏輕步兵」（700）上。拖曳途中會顯示勝負預覽。",
                    Hint = "請用「雪人巨獸」攻擊「巡邏輕步兵」。" },
                new() { Title = "攻擊後橫置", Spot = ["own-card:WCG-114"],
                    Text = "攻擊過的怪物會橫置，這回合不能再攻擊；少數卡牌效果可以讓怪物重新直立、再攻擊一次。" },
                new() { Title = "平手同歸於盡", Spot = ["own-card:WCG-154", "enemy-card:WCG-156"], Allow = [Attack("WCG-154", "WCG-156")],
                    Done = e => !OnField(e.Computer, "WCG-156") && !OnField(e.Player, "WCG-154"),
                    Text = "「淡水狂鱷」與「荒原巨槌食人魔」都是 1500 PP。讓它們交戰，看看平手時會發生什麼。",
                    Hint = "請用「淡水狂鱷」攻擊「荒原巨槌食人魔」。" },
                new() { Title = "墓地與對戰紀錄", Spot = ["history"],
                    Text = "被消滅的卡會進入墓地。按「紀錄／墓地」可以查看雙方墓地與完整對戰紀錄；看完記得關閉視窗。" },
                new() { Title = "略過怪物，直接打玩家", Spot = ["own-card:WCG-155", "face"], Allow = [Attack("WCG-155")],
                    Done = e => e.Computer.Hp == 6,
                    Text = "對手沒有嘲諷怪物時，你可以不理會敵方怪物，直接攻擊玩家。「蒼翠古樹衛士」有 2300 PP，硬碰只會讓「鋼鐵構裝體」被消滅，所以改打電腦頭像。",
                    Hint = "請把「鋼鐵構裝體」拖到電腦頭像。" },
                new() { Title = "本課重點",
                    Text = "出手前先比 PP：贏了換掉對方、平手一換一、輸了只會白白損失。能量、手牌與場上格位都是資源，好的交換能讓你掌握戰場。" }
            ]
        },
        new()
        {
            Id = "taunt", Icon = "shield", Title = "嘲諷：保護與突破",
            Summary = "嘲諷怪物會擋下攻擊；學會用法術越過嘲諷，再打穿防線。",
            Topics = ["嘲諷", "法術不受嘲諷限制", "指定目標法術", "DP 傷害"],
            Scene = new(
                new() { Hand = ["WCG-002"], Deck = Deck(), Energy = 2, Board = [new("WCG-114", 1), new("WCG-156", 3)] },
                new() { Hp = 4, Deck = Deck(), Energy = 1, Board = [new("WCG-101", 0), new("WCG-049", 2)] }),
            Steps =
            [
                new() { Title = "嘲諷", Spot = ["enemy-card:WCG-049", "face"],
                    Text = "敵方場上有嘲諷怪物時，你的怪物只能攻擊嘲諷怪物，不能攻擊其他怪物或玩家。嘲諷卡會有金色盾框，電腦頭像也會顯示「嘲諷阻擋」。" },
                new() { Title = "法術越過嘲諷", Spot = ["hand:WCG-002", "enemy-card:WCG-101"], Allow = [Play("WCG-002", "WCG-101"), Pick("WCG-101")],
                    Done = e => !OnField(e.Computer, "WCG-101"),
                    Text = "嘲諷只限制怪物的攻擊，法術可以指定任何合法目標。把「致死衝擊」拖到嘲諷後方的「巡邏輕步兵」上消滅它。",
                    Hint = "請把「致死衝擊」拖到「巡邏輕步兵」上。" },
                new() { Title = "擊破嘲諷", Spot = ["own-card:WCG-114", "enemy-card:WCG-049"], Allow = [Attack("WCG-114", "WCG-049")],
                    Done = e => !OnField(e.Computer, "WCG-049"),
                    Text = "用「雪人巨獸」（2200）攻擊「鐵木樹人」（1800），擊破嘲諷。",
                    Hint = "有嘲諷時只能攻擊「鐵木樹人」。" },
                new() { Title = "打穿防線", Spot = ["own-card:WCG-156", "face"], Allow = [Attack("WCG-156")],
                    Done = e => e.Computer.Hp == 2,
                    Text = "嘲諷消失了。「荒原巨槌食人魔」的 DP 是 2，攻擊電腦頭像會造成 2 點傷害。",
                    Hint = "請把「荒原巨槌食人魔」拖到電腦頭像。" },
                new() { Title = "本課重點",
                    Text = "突破嘲諷的方法：用 PP 更高的怪物擊破、用法術消滅，或用沉默讓它失去嘲諷。少數卡牌能無視特定嘲諷，也有卡牌專門消滅嘲諷怪物。" }
            ]
        },
        new()
        {
            Id = "shield", Icon = "shield", Title = "聖盾與箭頭",
            Summary = "聖盾要付能量才會生效；學會用箭頭把聖盾交給相鄰的隊友。",
            Topics = ["聖盾", "橫置能量保命", "箭頭 ← → ↑", "固定格位"],
            Scene = new(
                new() { Hand = ["WCG-061"], Deck = Deck(), Energy = 4, Board = [new("WCG-154", 2)] },
                new() { Deck = Deck(), Energy = 1, Board = [new("WCG-155", 1)] }),
            Steps =
            [
                new() { Title = "聖盾", Spot = ["own-card:WCG-154"],
                    Text = "具聖盾的怪物即將在交戰中被消滅時，控制者可以橫置 1 點直立能量讓它存活。每次都要付能量，沒有能量就保不住。" },
                new() { Title = "放對格位", Spot = ["hand:WCG-061", "own:1"], Allow = [Play("WCG-061"), Slot(1)],
                    Done = e => Unit(e.Player, "WCG-061")?.Slot == 1,
                    Text = "「銀色誓言衛」有 → 箭頭：右側相鄰的己方怪物具有聖盾。把它放在第 2 格，也就是「淡水狂鱷」的左邊。",
                    Hint = "請把「銀色誓言衛」放在第 2 格（淡水狂鱷左邊）。" },
                new() { Title = "箭頭", Spot = ["own-card:WCG-061", "own-card:WCG-154"],
                    Text = "→ 指向右側相鄰格，← 指向左側相鄰格，↑ 指向正對面的敵方格。格位進場後不能換，所以放在哪裡很重要。現在「淡水狂鱷」獲得了聖盾。" },
                new() { Title = "付能量保命", Spot = ["own-card:WCG-154", "enemy-card:WCG-155", "dialog"], Allow = [Attack("WCG-154", "WCG-155"), Choose("SHIELD")],
                    Done = e => Unit(e.Player, "WCG-154") is { IsTapped: true } && e.Player.AvailableEnergy == 1 && Settled(e),
                    Text = "用「淡水狂鱷」（1500）攻擊「鋼鐵構裝體」（1800）。它本該被消滅，出現提示時選擇「橫置1點能量使用聖盾」。",
                    Hint = "請攻擊「鋼鐵構裝體」，並選擇使用聖盾。" },
                new() { Title = "本課重點",
                    Text = "聖盾只防交戰中的消滅，不擋法術消滅、犧牲或返回手牌。有的怪物天生具有聖盾，也有附著法術或結界能給予聖盾；沉默會讓怪物失去自身的聖盾。" }
            ]
        },
        new()
        {
            Id = "arrows", Icon = "compass", Title = "箭頭與站位",
            Summary = "箭頭不只給聖盾：用左右箭頭強化相鄰隊友，用上箭頭攻擊正對面的敵人。",
            Topics = ["箭頭 ← → ↑", "相鄰格光環", "正對面同列格", "五大意志的箭頭"],
            Scene = new(
                new() { Hand = ["WCG-005", "WCG-156"], Deck = Deck(), Energy = 5, Board = [new("WCG-101", 1), new("WCG-154", 3)] },
                new() { Deck = Deck(), Energy = 1, Board = [new("WCG-132", 0), new("WCG-155", 2)] }),
            Steps =
            [
                new() { Title = "箭頭連結格位", Spot = ["face", "own"],
                    Text = "卡圖邊緣的金色三角就是箭頭：← → 連到己方左右相鄰格，↑ 連到正對面的敵方格。每張卡用箭頭做的事不同，看效果文字就知道。" },
                new() { Title = "左右光環", Spot = ["hand:WCG-005", "own:2"], Allow = [Play("WCG-005"), Slot(2)],
                    Done = e => Unit(e.Player, "WCG-005")?.Slot == 2,
                    Text = "「突擊狼騎兵」的 ← → 讓左右相鄰的己方怪物交戰時 PP +200。把它放在第 3 格，夾在「巡邏輕步兵」和「淡水狂鱷」中間。",
                    Hint = "請把「突擊狼騎兵」放在第 3 格。" },
                new() { Title = "站在中間最划算", Spot = ["own-card:WCG-101", "own-card:WCG-154"],
                    Text = "兩側隊友都變強了：700→900、1500→1700。放在邊格只能照顧一側；來源被沉默或離場，加成立刻消失。" },
                new() { Title = "正對面", Spot = ["hand:WCG-156", "own:4", "enemy-card:WCG-132"], Allow = [Play("WCG-156"), Slot(4)],
                    Done = e => !OnField(e.Computer, "WCG-132") && Settled(e),
                    Text = "「荒原巨槌食人魔」的 ↑ 進場時消滅正對面 PP 800 以下的敵方怪物。己方第 5 格正對敵方第 1 格的「腐肉食腐蛛」，把它放在第 5 格。",
                    Hint = "請把「荒原巨槌食人魔」放在第 5 格（腐肉食腐蛛的正對面）。" },
                new() { Title = "本課重點",
                    Text = "格位進場後不能換，箭頭讓站位成為策略：狂怒用 ↑ 劈砍正對面，理智依站位抽牌或橫置對手，生機用光環培育隊友，秩序用箭頭分配聖盾，深淵與中立壓低正對面敵怪的 PP。" }
            ]
        },
        new()
        {
            Id = "spells", Icon = "spark", Title = "法術：目標、抉擇與回復",
            Summary = "施放指定目標的法術、從抉擇效果二選一、棄牌與回復生命。",
            Topics = ["指定目標法術", "抽牌", "抉擇", "棄牌", "回復生命（上限 7）", "法術進墓地"],
            Scene = new(
                new() { Hp = 4, Hand = ["WCG-004", "WCG-046", "WCG-050"], Deck = Deck(), Energy = 5 },
                new() { Deck = Deck(), Energy = 1, Board = [new("WCG-101", 0), new("WCG-132", 2)] }),
            Steps =
            [
                new() { Title = "法術", Spot = ["hand"],
                    Text = "法術只能在自己的回合施放，支付費用後立即結算並進入墓地。需要目標的法術要拖到亮起的目標上；不需要目標的拖到戰場中央或對手頭像。" },
                new() { Title = "消滅並抽牌", Spot = ["hand:WCG-004", "enemy-card:WCG-132"], Allow = [Play("WCG-004", "WCG-132"), Pick("WCG-132")],
                    Done = e => !OnField(e.Computer, "WCG-132") && Settled(e),
                    Text = "「熾炎猛擊」消滅 1 隻 PP 500 以下的敵方怪物，然後抽 1 張牌。拖到「腐肉食腐蛛」上。",
                    Hint = "請把「熾炎猛擊」拖到「腐肉食腐蛛」上。" },
                new() { Title = "抉擇", Spot = ["hand:WCG-046", "dialog"], Allow = [Play("WCG-046"), Choose("LOOT"), ChooseCard("WCG-101"), ChooseCard("WCG-154")],
                    Done = e => e.Player.Graveyard.Any(c => c.Card.Id == "WCG-046") && e.Player.Graveyard.Count >= 3 && Settled(e),
                    Text = "「自然之怒」是抉擇法術，打出時二選一。場上已沒有 PP 500 以下的敵怪，請選「抽 1 張牌，然後棄 1 張手牌」，再棄掉剛抽到的「巡邏輕步兵」或「淡水狂鱷」（留下「甦生之雨」）。",
                    Hint = "請打出「自然之怒」，選擇抽 1 棄 1，並棄掉剛抽到的怪物。" },
                new() { Title = "回復生命", Spot = ["hand:WCG-050", "hero"], Allow = [Play("WCG-050")],
                    Done = e => e.Player.Hp == 6,
                    Text = "「甦生之雨」回復 2 點生命。生命上限是 7，超過的部分不會累積。",
                    Hint = "請打出「甦生之雨」。" },
                new() { Title = "本課重點",
                    Text = "常見法術效果：直接傷害、消滅、抽牌、回復、返回手牌、橫置或直立怪物、附著在怪物上。所有費用都是通用能量，任何意志的卡都能用同一池能量支付。" }
            ]
        },
        new()
        {
            Id = "triggers", Icon = "bolt", Title = "進場、離場與非付費召喚",
            Summary = "認識進場與離場效果、免費召喚，以及效果依序結算的方式。",
            Topics = ["進場效果", "離場效果", "非付費召喚不觸發進場", "效果依序結算"],
            Scene = new(
                new() { Hand = ["WCG-047", "WCG-003", "WCG-007"], Deck = Deck(), Energy = 4, Board = [new("WCG-083", 4)] },
                new() { Deck = Deck(), Energy = 1, Graveyard = ["WCG-101"], Board = [new("WCG-158", 0), new("WCG-132", 1), new("WCG-043", 3)] }),
            Steps =
            [
                new() { Title = "觸發效果", Spot = ["hand"],
                    Text = "「進場」在付費召喚後觸發；「離場」在怪物離開戰場時觸發；「每當」則在條件達成時觸發。效果會一個接一個結算，需要選擇時畫面會停下來等你。" },
                new() { Title = "進場：免費召喚", Spot = ["hand:WCG-047", "dialog"], Allow = [Play("WCG-047"), Slot(), ChooseCard("WCG-003")],
                    Done = e => OnField(e.Player, "WCG-047") && OnField(e.Player, "WCG-003") && Settled(e),
                    Text = "召喚「繁衍蜂群」。它的進場讓你從手牌免費召喚 1 張費用 1 以下的怪物：選「熔岩地精」，再為兩隻怪物各選一個空格。",
                    Hint = "請召喚「繁衍蜂群」，並用進場效果免費召喚「熔岩地精」。" },
                new() { Title = "非付費召喚", Spot = ["own-card:WCG-003"],
                    Text = "「熔岩地精」是被效果召喚的，沒有支付費用，所以不會觸發它的進場效果；離場效果則照常有效。" },
                new() { Title = "離場效果", Spot = ["own-card:WCG-083", "enemy-card:WCG-158"], Allow = [Attack("WCG-083", "WCG-158"), Pick("WCG-132")],
                    Done = e => !OnField(e.Computer, "WCG-132") && !OnField(e.Player, "WCG-083") && Settled(e),
                    Text = "讓「腐爛食屍鬼」（500）攻擊「白銀持戟禁衛」（2200）。它會被消滅並觸發離場：選擇消滅「腐肉食腐蛛」。",
                    Hint = "請用「腐爛食屍鬼」攻擊「白銀持戟禁衛」，離場時選擇「腐肉食腐蛛」。" },
                new() { Title = "進場：消滅", Spot = ["hand:WCG-007", "enemy-card:WCG-043"], Allow = [Play("WCG-007"), Slot(), Pick("WCG-043")],
                    Done = e => !OnField(e.Computer, "WCG-043") && OnField(e.Player, "WCG-007") && Settled(e),
                    Text = "召喚「狂血投擲者」，它的進場會消滅 PP 最低且在 500 以下的敵方怪物：「孢子幼芽」。",
                    Hint = "請召喚「狂血投擲者」並選擇「孢子幼芽」。" },
                new() { Title = "本課重點",
                    Text = "對手的「孢子幼芽」離場時也觸發了自己的效果。付費召喚才會觸發進場；被消滅、犧牲或返回手牌都算離場；同時發生的多個效果會依序結算。" }
            ]
        },
        new()
        {
            Id = "keywords", Icon = "flame", Title = "劇毒、貫穿與衝鋒",
            Summary = "三個戰鬥關鍵字：以小博大的劇毒、打穿到玩家的貫穿、即刻出擊的衝鋒。",
            Topics = ["劇毒", "貫穿", "衝鋒", "冰凍與潛伏（目前卡池未使用）"],
            Scene = new(
                new() { Hand = ["WCG-110"], Deck = Deck(), Energy = 3, Board = [new("WCG-051", 0), new("WCG-053", 2)] },
                new() { Deck = Deck(), Energy = 1, Board = [new("WCG-154", 2), new("WCG-157", 4)] }),
            Steps =
            [
                new() { Title = "戰鬥關鍵字", Spot = ["own-card:WCG-051", "own-card:WCG-053"],
                    Text = "劇毒：與它交戰的怪物無論 PP 多高都會被消滅。貫穿：攻擊方存活並消滅防守方，且 PP 差距達 700 以上時，對敵方玩家造成 1 點傷害。" },
                new() { Title = "劇毒", Spot = ["own-card:WCG-051", "enemy-card:WCG-157"], Allow = [Attack("WCG-051", "WCG-157")],
                    Done = e => !OnField(e.Computer, "WCG-157"),
                    Text = "用「猛毒荊棘蜥」（700）攻擊「蒼翠古樹衛士」（2300）。PP 較低的蜥蜴會被消滅，但劇毒也會帶走對方。",
                    Hint = "請用「猛毒荊棘蜥」攻擊「蒼翠古樹衛士」。" },
                new() { Title = "貫穿", Spot = ["own-card:WCG-053", "enemy-card:WCG-154"], Allow = [Attack("WCG-053", "WCG-154")],
                    Done = e => !OnField(e.Computer, "WCG-154") && e.Computer.Hp == 6,
                    Text = "用「奔竄巨犀」（2200）攻擊「淡水狂鱷」（1500）。差距剛好 700，貫穿會額外對電腦造成 1 點傷害。",
                    Hint = "請用「奔竄巨犀」攻擊「淡水狂鱷」。" },
                new() { Title = "衝鋒", Spot = ["hand:WCG-110"], Allow = [Play("WCG-110"), Slot()],
                    Done = e => OnField(e.Player, "WCG-110"),
                    Text = "召喚具衝鋒的「狼騎兵先遣隊」，它進場就是直立的。",
                    Hint = "請召喚「狼騎兵先遣隊」。" },
                new() { Title = "立即出擊", Spot = ["own-card:WCG-110", "face"], Allow = [Attack("WCG-110")],
                    Done = e => e.Computer.Hp == 5,
                    Text = "用「狼騎兵先遣隊」攻擊電腦頭像。",
                    Hint = "請把「狼騎兵先遣隊」拖到電腦頭像。" },
                new() { Title = "本課重點",
                    Text = "關鍵字會被沉默移除。規則書另保留「冰凍」與「潛伏」兩個關鍵字，但目前 199 張卡池中沒有卡牌使用它們。" }
            ]
        },
        new()
        {
            Id = "traps", Icon = "sigil", Title = "結界與蓋牌反擊",
            Summary = "放置並發動結界、背面蓋牌，在被攻擊時翻開反擊法術。",
            Topics = ["結界（主動／被動）", "發動結界", "蓋牌（0 費）", "被攻擊時翻開", "反擊法術", "電腦回合"],
            Scene = new(
                new() { Hand = ["WCG-151", "WCG-195", "WCG-101"], Deck = Deck(), Energy = 6 },
                new() { Deck = Deck(), Board = [new("WCG-154", 0), new("WCG-101", 1)] }),
            ComputerMoves = [new("attack", "WCG-154")],
            Steps =
            [
                new() { Title = "結界", Spot = ["hand:WCG-151"],
                    Text = "結界是留在場上的卡，佔用 1 個格位但不參與交戰。主動結界要橫置才能發動效果，下個自己的回合重新直立；被動結界放在場上就會自動生效。" },
                new() { Title = "放置結界", Spot = ["hand:WCG-151"], Allow = [Play("WCG-151"), Slot()],
                    Done = e => OnBoard(e.Player, "WCG-151"),
                    Text = "把「熔火鍛爐」（費用 3）拖到己方空格。",
                    Hint = "請把「熔火鍛爐」拖到己方空格。" },
                new() { Title = "發動結界", Spot = ["own-card:WCG-151", "dialog", "enemy-card:WCG-101"], Allow = [Activate("WCG-151"), Pick("WCG-101")],
                    Done = e => !OnField(e.Computer, "WCG-101"),
                    Text = "點一下場上的「熔火鍛爐」打開詳情，按「發動結界」，再點選敵方「巡邏輕步兵」消滅它。",
                    Hint = "請發動「熔火鍛爐」，選擇「巡邏輕步兵」。" },
                new() { Title = "蓋牌", Spot = ["hand:WCG-195"],
                    Text = "任何手牌都能以 0 費背面蓋在己方空格。當你被宣告攻擊時，可以翻開 1 張蓋牌：若是付得起的反擊法術就會發動，否則只會進入墓地。反擊法術不能正常施放。" },
                new() { Title = "蓋下反擊法術", Spot = ["hand:WCG-195"], Allow = [Set("WCG-195"), Slot()],
                    Done = e => e.Player.Structures.Any(m => m.IsSet && m.Card.Id == "WCG-195"),
                    Text = "對「熔火爆炸陷阱」按右鍵（手機請點牌放大後按「切換為蓋牌」）切換成背面，再拖到己方空格。",
                    Hint = "請把「熔火爆炸陷阱」切換為蓋牌，再拖到己方空格。" },
                new() { Title = "結束回合", Spot = ["end"], Allow = [End()],
                    Done = e => e.CurrentTurnPlayerId == "computer",
                    Text = "能量還留著 3 點，足夠支付反擊法術。按「結束回合」。",
                    Hint = "請按「結束回合」。" },
                new() { Title = "翻開蓋牌！", Spot = ["dialog"], Allow = [ChooseCard("WCG-195")],
                    Done = e => !OnField(e.Computer, "WCG-154") && Settled(e),
                    Text = "電腦會宣告攻擊你。出現提示時，選擇翻開你的蓋牌。",
                    Hint = "請選擇翻開蓋牌。" },
                new() { Title = "回到你的回合", Spot = ["face"], Done = e => e.CurrentTurnPlayerId == "player" && Settled(e),
                    Text = "反擊成功！「熔火爆炸陷阱」消滅了攻擊者，且它的 PP 在 1500 以上，對電腦造成 1 點傷害。電腦正在結束回合……" },
                new() { Title = "本課重點",
                    Text = "蓋牌不會透露身分，對手只看得到背面。被動結界（例如「血怒祭壇」）會在條件達成時自動觸發；可以用驅散效果摧毀結界。" }
            ]
        },
        new()
        {
            Id = "status", Icon = "eye", Title = "附著、沉默與狀態",
            Summary = "用附著法術強化怪物，用沉默拔掉嘲諷與聖盾，再一舉擊破。",
            Topics = ["附著法術", "沉默", "驅散", "PP 加成", "電腦使用聖盾"],
            Scene = new(
                new() { Hand = ["WCG-135", "WCG-072"], Deck = Deck(), Energy = 6, Board = [new("WCG-154", 2)] },
                new() { Hp = 3, Deck = Deck(), Energy = 2, Board = [new("WCG-069", 2)] }),
            Steps =
            [
                new() { Title = "附著", Spot = ["hand:WCG-135", "own-card:WCG-154"],
                    Text = "附著法術會留在指定的怪物身上持續生效，怪物離場時一起進入墓地。點場上怪物可以在詳情中看到附著卡。" },
                new() { Title = "強化怪物", Spot = ["hand:WCG-135", "own-card:WCG-154"], Allow = [Play("WCG-135", "WCG-154"), Pick("WCG-154")],
                    Done = e => Unit(e.Player, "WCG-154") is { Attachments.Count: > 0 },
                    Text = "把「野性萌發」拖到「淡水狂鱷」上，PP +500 變成 2000。",
                    Hint = "請把「野性萌發」拖到「淡水狂鱷」上。" },
                new() { Title = "棘手的守衛", Spot = ["enemy-card:WCG-069"],
                    Text = "「鐵壁騎士」（1800）同時有嘲諷與聖盾。就算你打贏它，電腦也能橫置能量保住它。" },
                new() { Title = "沉默", Spot = ["hand:WCG-072", "enemy-card:WCG-069"], Allow = [Play("WCG-072", "WCG-069"), Pick("WCG-069")],
                    Done = e => Unit(e.Computer, "WCG-069") is { IsSilenced: true } && Settled(e),
                    Text = "沉默會移除怪物卡面上的所有能力，包括嘲諷與聖盾。把「淨化聖光」拖到「鐵壁騎士」上，然後抽 1 張牌。",
                    Hint = "請把「淨化聖光」拖到「鐵壁騎士」上。" },
                new() { Title = "擊破", Spot = ["own-card:WCG-154", "enemy-card:WCG-069"], Allow = [Attack("WCG-154", "WCG-069")],
                    Done = e => !OnField(e.Computer, "WCG-069"),
                    Text = "現在用 2000 PP 的「淡水狂鱷」攻擊失去聖盾的「鐵壁騎士」。",
                    Hint = "請用「淡水狂鱷」攻擊「鐵壁騎士」。" },
                new() { Title = "本課重點",
                    Text = "驅散能移除怪物身上的附著卡與沉默，或摧毀結界。其他狀態效果包括：下一次交戰 PP 加成、本回合 PP 加成、讓怪物無法攻擊的枷鎖、橫置或重新直立怪物，以及橫置對手的能量。" }
            ]
        },
        new()
        {
            Id = "costs", Icon = "heart", Title = "額外代價與勝利條件",
            Summary = "犧牲怪物、支付生命換取效果，最後讓電腦牌庫抽空而落敗。",
            Topics = ["額外代價：犧牲", "以生命換取資源", "牌庫抽空落敗", "勝利條件總結"],
            Scene = new(
                new() { Hand = ["WCG-006", "WCG-019"], Deck = Deck(), Energy = 4, Board = [new("WCG-101", 0)] },
                new() { Hp = 4, Hand = ["WCG-101"], Deck = [], Energy = 1 }),
            Steps =
            [
                new() { Title = "額外代價", Spot = ["hand:WCG-006"],
                    Text = "有些卡除了能量，還要付「額外代價」：犧牲己方怪物、隨機棄手牌或扣除生命。代價在效果之前支付，犧牲不算被消滅。" },
                new() { Title = "犧牲", Spot = ["hand:WCG-006", "own-card:WCG-101"], Allow = [Play("WCG-006"), Pick("WCG-101")],
                    Done = e => e.Computer.Hp == 2 && Settled(e),
                    Text = "打出「怒意沸騰」，選擇犧牲「巡邏輕步兵」，對電腦造成 2 點傷害。",
                    Hint = "請打出「怒意沸騰」並犧牲「巡邏輕步兵」。" },
                new() { Title = "以生命換牌", Spot = ["hand:WCG-019", "hero"], Allow = [Play("WCG-019")],
                    Done = e => e.Player.Hp == 5 && Settled(e),
                    Text = "生命也是資源。「燃血召喚」扣除你 2 點生命，抽 2 張牌。",
                    Hint = "請打出「燃血召喚」。" },
                new() { Title = "牌庫抽空", Spot = ["face"],
                    Text = "看看電腦頭像：牌庫剩 0 張。回合開始時需要抽牌卻沒有牌，就會直接落敗。" },
                new() { Title = "結束回合", Spot = ["end"], Allow = [End()],
                    Done = e => e.Computer.HasLost,
                    Text = "按「結束回合」，讓電腦在空牌庫中抽牌。",
                    Hint = "請按「結束回合」。" },
                new() { Title = "本課重點",
                    Text = "勝利條件：對手生命歸零，或對手需要抽牌時牌庫已空；投降則直接落敗。抽牌與棄牌效果要留意自己的牌庫張數。" }
            ]
        },
        new()
        {
            Id = "deckbuilding", Icon = "cards", Title = "牌組構築與五大意志",
            Summary = "在牌組構築室認識 50 張規則、單色／雙色、五大意志與儲存方式。",
            Topics = ["50 張牌組", "每張最多 4 張", "最多兩個派系（中立不計）", "主色", "單色／雙色", "五大意志", "篩選與搜尋", "預設牌組", "列印"],
            Route = "deckbuilder",
            Tour =
            [
                new("牌組構築室", "每副牌組必須剛好 50 張，同名卡最多 4 張，最多使用兩個意志（中立不計）。這次導覽不會改動你已儲存的牌組。", ".deck-page .page-hero"),
                new("建立空白牌組", "按「新建」開始一副全新的牌組。", ".deck-panel-card .card-footer .btn-outline-secondary", "deck-new"),
                new("選擇主色", "從「主色」選單選一個意志作為這副牌的主色。", "#main-will", "deck-main"),
                new("單色或雙色", "單色只放主色與中立；雙色可再加一個副色。卡牌庫會自動只顯示這副牌能放入的卡，鎖住的意志會顯示鎖頭。", ".color-mode"),
                new("五大意志", "狂怒：直接傷害與速攻。理智：抽牌與控制。生機：鋪場與成長。秩序：嘲諷、聖盾與保護。深淵：消滅、墓地與犧牲。中立卡任何牌組都能使用。", ".filter-panel .filter-row"),
                new("加入卡片", "按任一張卡下方的「＋ 加入」，把它放進牌組。點卡面可以查看完整效果。", ".add-card-btn", "deck-add"),
                new("張數與統計", "牌組面板會即時顯示張數進度、派系數與怪物／法術比例。湊滿 50 張才能儲存與出戰。", ".deck-panel-card .card-header"),
                new("載入、儲存與列印", "「選擇 / 載入牌組」可以複製官方五套預設牌組再修改；「儲存牌組」會檢查規則後存下；也能列印實體牌組。", ".deck-panel-card .card-footer")
            ]
        },
        new()
        {
            Id = "ranked", Icon = "trophy", Title = "天梯、對戰紀錄與回顧",
            Summary = "認識牌位與星數、出戰準備、對戰紀錄、對手牌組與逐步回顧。",
            Topics = ["牌位：青銅到大師", "每小段 5 星", "勝 +1／敗 −1、青銅保護", "每月賽季降段", "電腦強度隨牌位", "對戰紀錄", "對手牌組", "對局回顧"],
            Route = "ranked",
            Tour =
            [
                new("目前牌位", "天梯分為青銅、白銀、黃金、白金、鑽石、大師。每個大牌位有 III、II、I 三個小段，每小段 5 星。", ".ranked-card"),
                new("星數", "勝利加 1 星、敗北扣 1 星；集滿 5 星晉升下一小段。青銅不扣星，同一賽季也不會跌出已到達的大牌位。", ".ranked-card"),
                new("出戰準備", "選擇一副合法牌組開始天梯對戰。牌位越高，電腦越聰明，對手牌組也來自該牌位的牌組池。對局中離開會保留進度，投降算敗北。", ".ranked-entry"),
                new("對戰紀錄", "每場天梯對局都會留下紀錄。切換到「全部」看看所有賽季的紀錄。", ".ranked-records-head .segmented", "ranked-all"),
                new("對手牌組與回顧", "每筆紀錄可以展開「對手牌組」查看完整卡表，或按「對局回顧」逐步重播當局（只顯示你當時看得到的資訊）。", ".ranked-records"),
                new("賽季", "每個月是一個賽季。新賽季開始時下降兩個大牌位，最低青銅；歷季成績會保留在下方。", ".ranked-info")
            ]
        },
        new()
        {
            Id = "settings", Icon = "gear", Title = "設定、圖鑑與規則",
            Summary = "調整操作、音效與頭像，並認識完整卡牌圖鑑與官方規則頁。",
            Topics = ["填能量確認", "音效與音量", "特效精簡", "頭像", "存檔備份", "卡牌圖鑑", "官方規則"],
            Route = "settings",
            Tour =
            [
                new("操作設定", "「填能量前再次確認」可以避免誤把重要的牌拖進能量區；熟練後可以關閉。", ".settings-page .settings-panel"),
                new("音效", "可以調整音效與音量。對戰畫面右上角也有靜音與「特效／精簡」切換。", ".settings-page .audio-settings"),
                new("頭像", "選擇內建頭像或上傳自己的圖片，會顯示在對戰與天梯中。", ".settings-page .avatar-picker"),
                new("存檔與備份", "牌組、天梯、頭像與教學進度都存在這台裝置。網頁版可以在設定頁最下方匯出或匯入存檔備份，換瀏覽器前記得先備份。", ".settings-page [aria-labelledby='local-data-title']"),
                new("卡牌圖鑑", "「完整卡牌圖鑑」列出全部卡牌，可以依意志、類型與費用篩選。", ".nav-link[href='cards']"),
                new("官方遊戲規則", "完整規則整理在「官方遊戲規則」。忘記細節時隨時回來查，也可以在「教學」重玩任何一課。", ".nav-link[href='rules']")
            ]
        }
    ];

    public static TutorialLesson? Find(string? id) => All.FirstOrDefault(l => l.Id == id);
    public static TutorialLesson? NextAfter(string id)
    {
        var index = All.ToList().FindIndex(l => l.Id == id);
        return index >= 0 && index + 1 < All.Count ? All[index + 1] : null;
    }
}
