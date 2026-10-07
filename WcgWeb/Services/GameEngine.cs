using WcgWeb.Models;

namespace WcgWeb.Services;

// A circuit owns this engine. Mutations are serialized and effects pause at explicit choices.
// Presentation owns cancellable AI pacing; the engine never starts background tasks.
public partial class GameEngine
{
    private readonly CardDatabase _cardDb;
    private Random _random;
    public int AiLevel { get; set; } = -1;
    internal void SetReplaySeed(int seed) => _random = new Random(seed);
    private readonly object _gate = new();
    private readonly LinkedList<Action> _effects = new();
    private int _mutationDepth;
    public Guid MatchId { get; private set; } = Guid.NewGuid();
    public long Revision { get; private set; }
    public string LastError { get; private set; } = "";
    public PlayerState Player { get; private set; } = new() { Id = "player", Name = "玩家" };
    public PlayerState Computer { get; private set; } = new() { Id = "computer", Name = "電腦", IsAi = true };
    public PlayerState ActivePlayer => CurrentTurnPlayerId == Player.Id ? Player : Computer;
    public PlayerState OpponentPlayer => GetOpponent(ActivePlayer);
    public string CurrentTurnPlayerId { get; private set; } = "";
    public int TurnNumber { get; private set; } = 1;
    public TurnPhase CurrentPhase { get; set; } = TurnPhase.NotStarted;
    public List<GameLogEntry> Logs { get; private set; } = new();
    public PendingChoice? CurrentPendingChoice { get; set; }
    public PendingTarget? CurrentPendingTarget { get; set; }
    public bool IsWaiting => CurrentPendingChoice != null || CurrentPendingTarget != null;
    public bool IsOver => CurrentPhase == TurnPhase.GameOver || Player.HasLost || Computer.HasLost;
    public string DecisionPlayerId => CurrentPendingChoice?.OwnerId ?? CurrentPendingTarget?.OwnerId ?? CurrentTurnPlayerId;
    public event Action? OnStateChanged;
    public GameEngine(CardDatabase cardDb) : this(cardDb, new Random()) { }
    public GameEngine(CardDatabase cardDb, Random random) { _cardDb = cardDb; _random = random; }

    private bool Fail(string reason) { LastError = reason; return false; }
    private bool Change(Func<bool> action)
    {
        bool result;
        bool publish;
        lock (_gate)
        {
            publish = _mutationDepth++ == 0;
            try
            {
                if (publish) { RefreshBoard(); LastError = ""; _presentation.Clear(); }
                result = action();
                if (publish && result) { Drain(); RefreshBoard(); Revision++; }
                if (publish && !result) _presentation.Clear();
            }
            finally { _mutationDepth--; }
        }
        if (publish) OnStateChanged?.Invoke();
        return result;
    }
    public GameActionResult ExecuteCommand(string actorId, Guid match, long revision, Func<bool> action, bool anyTurn = false)
    {
        bool success;
        lock (_gate)
        {
            if (match != MatchId || revision != Revision)
                return new(false, "stale", "畫面已更新，請依目前狀態重新操作。", MatchId, Revision);
            if (actorId != Player.Id || (!anyTurn && CurrentPhase != TurnPhase.NotStarted && DecisionPlayerId != actorId))
                return new(false, "turn", "目前不是你的操作時間。", MatchId, Revision);
            success = action();
        }
        return new(success, success ? "ok" : "invalid", success ? "" : LastError, MatchId, Revision);
    }
    private void Resolve(params Action[] actions)
    {
        for (int i = actions.Length - 1; i >= 0; i--) _effects.AddFirst(actions[i]);
    }
    private void Drain()
    {
        int limit = 10000;
        while (!IsOver && !IsWaiting && _effects.First != null)
        {
            if (--limit == 0) throw new InvalidOperationException("Effect loop exceeded safety bound.");
            var effect = _effects.First.Value; _effects.RemoveFirst(); effect(); RefreshBoard();
        }
        if (IsOver) { _effects.Clear(); CurrentPendingChoice = null; CurrentPendingTarget = null; }
    }
    private void Log(string message, string level = "info")
    { Logs.Insert(0, new(message, level) { ActionNumber = Revision + 1 }); if (Logs.Count > 150) Logs.RemoveAt(Logs.Count - 1); }
    private bool IsCurrent(PlayerState p) => ReferenceEquals(p, Player) || ReferenceEquals(p, Computer);
    private bool Main(PlayerState p) => IsCurrent(p) && ReferenceEquals(p, ActivePlayer) && !IsOver && !IsWaiting && CurrentPhase == TurnPhase.MainPhase;
    private bool Alive(MonsterInstance m) => Player.Board.Contains(m) || Computer.Board.Contains(m);
    private PlayerState Owner(MonsterInstance m) => Player.Board.Contains(m) ? Player : Computer;
    public PlayerState GetOpponent(PlayerState p) => ReferenceEquals(p, Player) ? Computer : Player;
    private void Heal(PlayerState p, int amount) { var before = p.Hp; p.Hp = Math.Min(PlayerState.MaxHp, p.Hp + amount); Present("heal", p, amount: p.Hp - before, label: "回復生命"); Log($"【{p.Name}】回復 {amount} 點生命，目前 {p.Hp}。", "action"); }
    private void Damage(PlayerState p, int amount)
    {
        if (IsOver || amount <= 0) return;
        p.Hp = Math.Max(0, p.Hp - amount); Present("damage", p, amount: amount, label: "生命傷害"); Log($"【{p.Name}】受到 {amount} 點傷害，目前 {p.Hp}。", "danger"); CheckLethal(p);
    }
    private void Lose(PlayerState p, string reason)
    { p.HasLost = true; p.LossReason = reason; CurrentPhase = TurnPhase.GameOver; Present("gameover", p, label: reason); Log($"【{p.Name}】敗北：{reason}", "victory"); }
    private void CheckLethal(PlayerState p) { if (p.Hp <= 0) Lose(p, "生命降至 0"); }
    private bool Draw(PlayerState p)
    {
        if (IsOver) return false;
        if (p.Deck.Count == 0) { Lose(p, "需要抽牌時牌庫已空"); return false; }
        var c = p.Deck[0]; p.Deck.RemoveAt(0); p.Hand.Add(c); Present("draw", p, instance: p == Player ? c.InstanceId : null, card: p == Player ? c.Card : null, label: "抽牌"); return true;
    }
    private void DrawMany(PlayerState p, int count) { for (int i = 0; i < count && !IsOver; i++) Draw(p); Log($"【{p.Name}】抽牌，目前手牌 {p.Hand.Count} 張。"); }
    public bool DrawCard(PlayerState p, bool log = true) => Change(() => IsCurrent(p) && Draw(p));
    public void StartGame(Deck playerDeck, Deck computerDeck, bool playerFirst = true)
    {
        if (!playerDeck.IsValid(_cardDb.GetCard, out var a)) throw new ArgumentException(a, nameof(playerDeck));
        if (!computerDeck.IsValid(_cardDb.GetCard, out var b)) throw new ArgumentException(b, nameof(computerDeck));
        Change(() =>
        {
            ClearMatch();
            foreach (var id in playerDeck.CardIds) Player.Deck.Add(new(_cardDb.GetCard(id)!));
            foreach (var id in computerDeck.CardIds) Computer.Deck.Add(new(_cardDb.GetCard(id)!));
            Shuffle(Player.Deck); Shuffle(Computer.Deck);
            CurrentTurnPlayerId = playerFirst ? Player.Id : Computer.Id; CurrentPhase = TurnPhase.MainPhase;
            Present("turn", ActivePlayer, label: "對局開始");
            for (int i = 0; i < 7; i++) { Draw(Player); Draw(Computer); }
            Log("對局開始：50 張牌組、起手 7 張；先攻第一回合不抽牌。", "action"); return true;
        });
    }
    private void Shuffle<T>(List<T> list)
    { for (int i = list.Count - 1; i > 0; i--) { var j = _random.Next(i + 1); (list[i], list[j]) = (list[j], list[i]); } }
    private void ClearMatch()
    {
        MatchId = Guid.NewGuid(); Revision = 0; TurnNumber = 1; LastAttack = null; plannedSlots.Clear();
        Player = new() { Id = "player", Name = "玩家" }; Computer = new() { Id = "computer", Name = "電腦", IsAi = true };
        CurrentTurnPlayerId = ""; CurrentPhase = TurnPhase.NotStarted;
        CurrentPendingChoice = null; CurrentPendingTarget = null; _effects.Clear(); Logs.Clear(); _presentation.Clear(); RevealedCards = []; RevealTitle = "";
    }
    public void ResetToNotStarted() => Change(() => { ClearMatch(); return true; });
    public void Surrender(PlayerState p) => Change(() => { if (!IsCurrent(p) || IsOver || CurrentPhase == TurnPhase.NotStarted) return false; Lose(p, "投降"); return true; });
    public string GetEnergyProblem(PlayerState p, CardInstance c)
    {
        if (!Main(p)) return "目前不是可填能量的行動階段，或仍有選擇待結算。";
        if (p.HasFilledEnergyThisTurn) return "本回合已填過能量。";
        if (!p.Hand.Contains(c)) return "卡牌已不在手牌。";
        if (!c.Card.IsMonster && !c.Card.IsSpell && !c.Card.IsEnchantment) return "此卡牌不能填能量。";
        return "";
    }
    public bool CanPlayEnergy(PlayerState p, CardInstance c) => GetEnergyProblem(p, c) == "";
    public bool PlayEnergy(PlayerState p, CardInstance c) => Change(() =>
    {
        if (!CanPlayEnergy(p, c)) return Fail("填能量限自己行動階段每回合一次，且不能有待結算選擇。");
        p.Hand.Remove(c); c.IsTapped = false; p.EnergyZone.Add(c); p.HasFilledEnergyThisTurn = true;
        Present("energy", p, c.InstanceId, label: "背面填能量");
        Log($"【{p.Name}】背面填入 1 張能量，不抽牌。", "action"); return true;
    });
    public bool CanPayCost(PlayerState p, CardDefinition c, out List<CardInstance> payment)
    {
        payment = new(); if (c.TotalCost < 0) return false;
        var cost = ActualCost(p, c);
        var options = p.EnergyZone.Where(e => !e.IsTapped).Distinct().Take(cost).ToList();
        if (options.Count != cost) return false; payment = options; return true;
    }
    private void Ask(PlayerState p, string title, IEnumerable<ChoiceOption> choices, Action<ChoiceOption> callback, bool cancel = false, Guid? sourceInstanceId = null)
    {
        var options = choices.ToList();
        if (options.Count == 0) return;
        CurrentPendingChoice = new() { OwnerId = p.Id, Title = title, Options = options, OnChoiceSelected = callback, CanCancel = cancel, SourceInstanceId = sourceInstanceId };
        Present("choice", p, label: p == Player ? title : "電腦選擇效果");
    }
    private void CardChoice(PlayerState p, string title, IEnumerable<CardInstance> candidates, Action<CardInstance> callback, bool optional = false)
    {
        var list = candidates.ToList(); if (list.Count == 0) return;
        var options = list.Select(c => new ChoiceOption { Id = c.InstanceId.ToString(), Title = c.Card.Name, PreviewCard = c.Card }).ToList();
        if (optional) options.Add(new() { Id = "SKIP", Title = "放棄此可選效果" });
        Ask(p, title, options, option => { if (option.Id != "SKIP") { var selected = list.First(c => c.InstanceId.ToString() == option.Id); callback(selected); } });
    }
    public bool SelectChoice(ChoiceOption option) => Change(() =>
    {
        var pending = CurrentPendingChoice;
        var canonical = pending?.Options.FirstOrDefault(o => o.Id == option.Id);
        if (pending == null || canonical == null || IsOver) return Fail("此選項已失效。");
        CurrentPendingChoice = null; pending.OnChoiceSelected?.Invoke(canonical); return true;
    });
    public bool SelectTarget(MonsterInstance target) => Change(() =>
    {
        var pending = CurrentPendingTarget;
        if (pending == null || !Alive(target) || pending.Validator?.Invoke(target) == false || IsOver) return Fail("此目標不合法或已離場。");
        CurrentPendingTarget = null; pending.OnTargetSelected(target); return true;
    });
    public bool CancelTargetOrChoice() => Change(() =>
    {
        if (CurrentPendingChoice?.CanCancel != true && CurrentPendingTarget?.CanCancel != true) return Fail("這是已支付卡牌的必要結算，請完成選擇。");
        CurrentPendingChoice = null; CurrentPendingTarget = null; _effects.Clear(); return true;
    });
    private List<MonsterInstance> Targetable(PlayerState p, IEnumerable<MonsterInstance> source) => source.Where(Alive).ToList();
    private void PickMonster(PlayerState p, string title, IEnumerable<MonsterInstance> candidates, Action<MonsterInstance> callback, bool cancel = false, Guid? sourceInstanceId = null)
    {
        var list = Targetable(p, candidates); if (list.Count == 0) return;
        CurrentPendingTarget = new() { OwnerId = p.Id, Title = title, CanCancel = cancel, SourceInstanceId = sourceInstanceId,
            Validator = m => list.Contains(m) && Targetable(p, new[] { m }).Count == 1, OnTargetSelected = callback };
        Present("target", p, label: p == Player ? title : "電腦選擇目標");
    }
    private void Loot(PlayerState p) { Resolve(() => DrawMany(p, 1), () => Discard(p)); }
    private void Discard(PlayerState p)
    { CardChoice(p, "選擇要棄掉的手牌", p.Hand, c => { p.Hand.Remove(c); p.Graveyard.Add(c); Present("discard", p, c.InstanceId, card: c.Card, label: "棄牌"); }); }
    private void RandomDiscard(PlayerState p, CardInstance source, int count)
    {
        for (int i = 0; i < count; i++)
        { var options = p.Hand.Where(c => c != source).ToList(); var c = options[_random.Next(options.Count)]; p.Hand.Remove(c); p.Graveyard.Add(c); Present("discard", p, c.InstanceId, card: c.Card, label: "棄牌代價"); }
    }
    private List<MonsterInstance> SpellTargets(PlayerState p, CardDefinition c)
    {
        var enemy = GetOpponent(p).Field; IEnumerable<MonsterInstance> candidates = c.Id switch
        {
            "WCG-002" or "WCG-086" => enemy.Where(m => m.CurrentPP <= 1000),
            "WCG-012" or "WCG-064" => enemy.Where(m => m.Card.TotalCost <= 2),
            "WCG-004" or "WCG-046" => enemy.Where(m => m.CurrentPP <= 500),
            "WCG-048" => enemy.Where(m => m.CurrentPP <= p.Field.Count * 500),
            "WCG-024" or "WCG-137" or "WCG-184" => enemy,
            "WCG-062" or "WCG-074" => p.Field.Where(CanShield),
            "WCG-017" or "WCG-032" => enemy.Where(m => m.CurrentPP <= 1700),
            "WCG-082" or "WCG-084" => enemy.Where(m => m.CurrentPP <= 1300),
            "WCG-076" => enemy.Where(m => m.CurrentPP >= 1700),
            "WCG-026" or "WCG-088" or "WCG-094" or "WCG-123" => enemy,
            "WCG-036" or "WCG-135" => p.Field,
            "WCG-139" or "WCG-140" or "WCG-141" or "WCG-142" or "WCG-168" or "WCG-165" => p.Field.Concat(enemy),
            "WCG-143" => p.Field.Concat(enemy).Where(m => m.IsTapped),
            "WCG-167" => p.Field.Where(m => m.IsTapped),
            "WCG-164" => p.Field.Where(m => !m.IsTapped),
            "WCG-169" => p.Field,
            "WCG-072" => p.Field.Concat(enemy).Where(m => !m.IsSilenced),
            _ => Array.Empty<MonsterInstance>()
        };
        return Targetable(p, candidates).Where(m => m.IsSilenced || m.Card.Id != "WCG-152").ToList();
    }
    private static bool NeedsSpellTarget(string id) => id is "WCG-135" or "WCG-137" or "WCG-139" or "WCG-140" or "WCG-141" or "WCG-142" or "WCG-143" or "WCG-164" or "WCG-165" or "WCG-167" or "WCG-168" or "WCG-169" or "WCG-184" or "WCG-004" or "WCG-048" or "WCG-062" or "WCG-064" or "WCG-074" or "WCG-002" or "WCG-012" or "WCG-017" or "WCG-024" or "WCG-026" or "WCG-032" or "WCG-036" or "WCG-072" or "WCG-076" or "WCG-082" or "WCG-084" or "WCG-086" or "WCG-088" or "WCG-094" or "WCG-123";
    public string GetPlayProblem(PlayerState p, CardInstance c)
    {
        if (!Main(p)) return "目前不是可出牌的行動階段，或仍有選擇待結算。";
        if (!p.Hand.Contains(c)) return "卡牌已不在手牌。";
        if (_cardDb.GetCard(c.Card.Id) != c.Card) return "卡牌定義已失效。";
        if (!c.Card.IsMonster && !c.Card.IsSpell && !c.Card.IsEnchantment) return "不支援此卡牌類型。";
        if ((c.Card.IsMonster || c.Card.IsEnchantment) && p.Occupied >= 5) return "場上已滿（最多 5 隻）。";
        if (!CanPayCost(p, c.Card, out _)) return $"可用能量不足，需要 {ActualCost(p,c.Card)}。";
        if (c.Card.IsCounter) return "反擊法術須先蓋牌，於玩家被攻擊時發動。";
        if(c.Card.Id=="WCG-166" && p.Hand.Count<2)return "需有另一張手牌可置底。";
        var discard = c.Card.Id == "WCG-095" ? 2 : c.Card.Id == "WCG-084" ? 1 : 0;
        if (p.Hand.Count - 1 < discard) return $"需要另外 {discard} 張手牌支付棄牌代價。";
        if (c.Card.Id is "WCG-006" or "WCG-088" or "WCG-096" && p.Field.Count == 0) return "需要犧牲 1 隻己方怪物。";
        if (c.Card.Id == "WCG-090" && !p.Graveyard.Any(x => x.Card.IsMonster)) return "墓地沒有可回收的怪物。";
        if (c.Card.Id == "WCG-096" && p.Hp < 1) return "生命不足以支付。";
        if (NeedsSpellTarget(c.Card.Id) && SpellTargets(p, c.Card).Count == 0) return "沒有符合卡面條件的合法目標。";
        return "";
    }
    public bool CanPlayCard(PlayerState p, CardInstance c) => GetPlayProblem(p, c) == "";
    // Describe the first step before payment using the same predicates as BeginPlay.
    private static bool NeedsSacrifice(string id) => id is "WCG-006" or "WCG-088" or "WCG-096";
    public static string GetPlayPreparation(CardDefinition card) => card.Id == "WCG-046" ? "choice"
        : NeedsSpellTarget(card.Id) ? "target" : NeedsSacrifice(card.Id) ? "sacrifice" : "none";
    public bool SummonMonster(PlayerState p, CardInstance c) => Change(() => c.Card.IsMonster ? BeginPlay(p, c) : Fail("不是怪物牌。"));
    public bool CastSpell(PlayerState p, CardInstance c) => Change(() => c.Card.IsSpell ? BeginPlay(p, c) : Fail("不是法術牌。"));
    public IReadOnlyList<MonsterInstance> GetDirectPlayTargets(PlayerState p, CardInstance c) =>
        c.Card.IsSpell && NeedsSpellTarget(c.Card.Id) && CanPlayCard(p, c) ? SpellTargets(p, c.Card) : [];
    public bool CastSpellAt(PlayerState p, CardInstance c, MonsterInstance target) => Change(() =>
        GetDirectPlayTargets(p, c).Contains(target) ? BeginPlay(p, c, initialTarget: target) : Fail("此目標不合法，未出牌或扣費。"));
    private bool BeginPlay(PlayerState p, CardInstance c, string mode = "", MonsterInstance? initialTarget = null)
    {
        var problem = GetPlayProblem(p, c); if (problem != "") return Fail(problem);
        if (c.Card.Id == "WCG-046" && mode == "")
        {
            var modes = new List<ChoiceOption> { new() { Id = "LOOT", Title = "抽 1 張牌，然後棄 1 張手牌" } };
            if (SpellTargets(p, c.Card).Count > 0) modes.Insert(0, new() { Id = "KILL", Title = "消滅 PP 500 以下敵怪" });
            Ask(p, "自然之怒抉擇（尚未扣費）", modes, o => BeginPlay(p, c, o.Id), true, c.InstanceId); return true;
        }
        void WithTarget(MonsterInstance? target)
        {
            if (NeedsSacrifice(c.Card.Id)) PickMonster(p, "選擇要犧牲的己方怪物（尚未扣費）", p.Field, sacrifice => CommitPlay(p, c, target, sacrifice, mode), true, c.InstanceId);
            else if (c.Card.IsMonster || c.Card.IsEnchantment)
                ChooseSlot(p, c.Card, slot => { plannedSlots[c.InstanceId] = slot; CommitPlay(p, c, target, null, mode); }, true, c.InstanceId);
            else CommitPlay(p, c, target, null, mode);
        }
        if (initialTarget != null) WithTarget(initialTarget);
        else if (NeedsSpellTarget(c.Card.Id) || c.Card.Id == "WCG-046" && mode == "KILL") PickMonster(p, "選擇卡牌目標（尚未扣費，可取消）", SpellTargets(p, c.Card), m => WithTarget(m), true, c.InstanceId);
        else WithTarget(null);
        return true;
    }
    private void CommitPlay(PlayerState p, CardInstance c, MonsterInstance? target, MonsterInstance? sacrifice, string mode)
    {
        if (GetPlayProblem(p, c) != "" || (target != null && !SpellTargets(p, c.Card).Contains(target)) || (sacrifice != null && !p.Field.Contains(sacrifice)))
        { Fail("卡牌或代價已失效，未扣費。"); return; }
        CanPayCost(p, c.Card, out var payment); foreach (var e in payment) e.IsTapped = true;
        Present("pay", p, amount: payment.Count, label: "支付能量");
        Present("play", p, c.InstanceId, target?.InstanceId, card: c.Card, label: c.Card.IsMonster ? "打出怪物" : "施放法術");
        var id = c.Card.Id;
        if (id is "WCG-084" or "WCG-095") RandomDiscard(p, c, id == "WCG-095" ? 2 : 1);
        p.Hand.Remove(c);
        if (c.Card.IsMonster && p.NextCreatureDiscount > 0) p.NextCreatureDiscount = 0;
        if (c.Card.IsSpell) p.Graveyard.Add(c);
        if (id == "WCG-164" && target != null) target.IsTapped = true;
        if (id == "WCG-165") { Damage(p, 1); if (IsOver) return; }
        if (id == "WCG-169" && target != null) KillBatch([target], "消滅代價");
        if (sacrifice != null) KillBatch(new[] { sacrifice }, "犧牲代價", destroyed: false);
        if (id == "WCG-096") { Damage(p, 1); if (IsOver) return; }
        // Cost-caused death triggers precede the original card effect.
        _effects.AddLast(() =>
        {
            if (c.Card.IsMonster) Summon(p, c, true);
            else if (c.Card.IsEnchantment) SummonStructure(p, c);
            else { Log($"【{p.Name}】施放【{c.Card.Name}】。", "action"); Spell(p, c, target, mode); UsedSpell(p, c.Card); }
        });
    }
    private void Summon(PlayerState p, CardInstance c, bool paid)
    {
        if (IsOver || p.Occupied >= 5) return;
        var slot = plannedSlots.Remove(c.InstanceId, out var chosen) ? chosen : FirstSlot(p);
        var m = new MonsterInstance(c.Card) { InstanceId = c.InstanceId, Slot = slot }; p.Field.Add(m); RefreshBoard(); Present("summon", p, c.InstanceId, card: c.Card, label: paid ? "召喚" : "免費召喚"); Log($"【{p.Name}】召喚【{c.Card.Name}】{(paid ? "" : "（非付費，不觸發進場）")}。", "action");
        Summoned(p, m);
        if (!paid) return;
        _effects.AddLast(() => Deploy(p, m));
        _effects.AddLast(() =>
        {
            foreach (var juggler in p.Field.Where(x => x != m && !x.IsSilenced && x.Card.Id == "WCG-107").ToArray())
                _effects.AddLast(() => PickLowest(p, GetOpponent(p).Field.Where(x => x.CurrentPP <= 500), x => KillBatch(new[] { x }, "飛刀投擲手")));
        });
    }
    private void UsedSpell(PlayerState p, CardDefinition? spell = null)
    {
        NewSpellTriggers(p, spell);
        foreach (var m in p.Field.Where(m => !m.IsSilenced).ToArray())
        {
            if (m.Card.Id == "WCG-023") _effects.AddLast(() => { if (Alive(m) && !m.IsSilenced) { Present("trigger", p, m.InstanceId, card: m.Card, label: "施法觸發"); Loot(p); } });
            if (m.Card.Id == "WCG-037") _effects.AddLast(() => { if (Alive(m) && !m.IsSilenced && GetOpponent(p).Hand.Count > p.Hand.Count) { Present("trigger", p, m.InstanceId, card: m.Card, label: "施法觸發"); DrawMany(p, 1); } });
        }
    }
    private void Bounce(MonsterInstance m)
    { if (!Alive(m)) return; var owner = Owner(m); ReleaseAttachments(m); owner.Field.Remove(m); owner.Hand.Add(new(m.Card) { InstanceId = m.InstanceId }); Present("bounce", owner, m.InstanceId, card: m.Card, label: "返回手牌"); Log($"【{m.Card.Name}】回到手牌。"); }
    private void PickLowest(PlayerState p, IEnumerable<MonsterInstance> candidates, Action<MonsterInstance> action)
    { var list = candidates.Where(Alive).ToList(); if (list.Count > 0) PickMonster(p, "選擇最低 PP 的怪物", list.Where(m => m.CurrentPP == list.Min(x => x.CurrentPP)), action); }
    private void PickHighest(PlayerState p, IEnumerable<MonsterInstance> candidates, Action<MonsterInstance> action)
    { var list = candidates.Where(Alive).ToList(); if (list.Count > 0) PickMonster(p, "選擇最高 PP 的怪物", list.Where(m => m.CurrentPP == list.Max(x => x.CurrentPP)), action); }
}
