using Microsoft.AspNetCore.Components;
using WcgWeb.Models;
using WcgWeb.Services;

namespace WcgWeb.Components.Pages;

public partial class DeckBuilder
{
    [SupplyParameterFromQuery(Name = "tour")] public string? Tour { get; set; }
    // The deck-building tour only reads page state; nothing is saved unless the player presses 儲存.
    private Dictionary<string, bool> TourChecks => new()
    {
        ["deck-new"] = CurrentDeck.CardIds.Count == 0 && string.IsNullOrEmpty(CurrentDeck.MainWill),
        ["deck-main"] = !string.IsNullOrEmpty(CurrentDeck.MainWill),
        ["deck-add"] = CurrentDeck.CardIds.Count > 0
    };
    private DeckPrintPlan? PrintPlan;
    private void OpenPrintPreview()
    {
        if (!ValidateDeck()) return;
        Inspect = null;
        PrintPlan = DeckPrintPlan.Create(CurrentDeck, CardDb);
    }
    private bool ShowDeckCards;
    private CardDefinition? Inspect;
    private void ResetFilters() { SearchText = ""; SelectedWill = ""; SelectedType = ""; SelectedCost = -1; }
    private Deck CurrentDeck { get; set; } = new Deck { Name = "我的自訂牌組" };
    private bool ShowDeckSelectModal { get; set; } = false;
    private string SearchText { get; set; } = "";
    private string SelectedWill { get; set; } = "";
    private string SelectedType { get; set; } = "";
    private int SelectedCost { get; set; } = -1;

    private string? ValidationErrorMessage { get; set; }
    private string? SavedMessage { get; set; }
    private int ToastId;
    private CancellationTokenSource? ToastTimer;

    // One success message: a toast that hides itself after a few seconds (also in reduced-motion mode).
    private void ShowSaved(string text)
    {
        SavedMessage = text; ToastId++;
        ToastTimer?.Cancel(); ToastTimer?.Dispose();
        ToastTimer = new CancellationTokenSource();
        _ = HideSavedLater(ToastId, ToastTimer.Token);
    }

    private async Task HideSavedLater(int id, CancellationToken token)
    {
        try { await Task.Delay(4200, token); } catch (TaskCanceledException) { return; }
        await InvokeAsync(() => { if (ToastId == id && SavedMessage != null) { SavedMessage = null; StateHasChanged(); } });
    }

    public void Dispose() { ToastTimer?.Cancel(); ToastTimer?.Dispose(); ToastTimer = null; }

    protected override void OnInitialized()
    {
        // Default to cloning the first preset deck so user immediately has a functional 50-card deck
        if (CardDb.PresetDecks.Count > 0)
        {
            ClonePreset(CardDb.PresetDecks[0]);
        }
    }

    private void SetWillFilter(string will) => SelectedWill = will;
    private void SetTypeFilter(string type) => SelectedType = type;

    private List<CardDefinition> FilteredCards
    {
        get
        {
            var list = CardDb.AllCards.AsEnumerable();
            if (ShowDeckCards)
            {
                var ids = CurrentDeck.CardIds.ToHashSet();
                list = list.Where(c => ids.Contains(c.Id));
            }

            // Locked to the deck's colours (主色＋副色＋中立) while browsing the library; 目前牌組 still lists every card in the deck.
            if (!ShowDeckCards && AllowedWills is { } allowed)
                list = list.Where(c => allowed.Contains(c.Will));
            if (!string.IsNullOrEmpty(SelectedWill))
                list = list.Where(c => c.Will == SelectedWill);

            if (!string.IsNullOrEmpty(SelectedType))
                list = list.Where(c => SelectedType=="法術"?c.IsSpell:c.Type == SelectedType);

            if (SelectedCost >= 0)
            {
                if (SelectedCost >= 7)
                    list = list.Where(c => c.TotalCost >= 7);
                else
                    list = list.Where(c => c.TotalCost == SelectedCost);
            }

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var q = SearchText.Trim();
                list = list.Where(c => c.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                       c.Text.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                       c.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                       L.CardName(c).Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                       L.CardText(c).Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            return list.OrderBy(c => c.IsEnergy ? 0 : 1)
                       .ThenBy(c => c.TotalCost)
                       .ThenBy(c => c.Id)
                       .ToList();
        }
    }

    private class DeckCardGroup
    {
        public CardDefinition Card { get; set; } = null!;
        public int Count { get; set; }
    }

    private List<DeckCardGroup> GroupedDeckCards
    {
        get
        {
            return CurrentDeck.CardIds
                .GroupBy(id => id)
                .Select(g => new DeckCardGroup
                {
                    Card = CardDb.GetCard(g.Key)!,
                    Count = g.Count()
                })
                .Where(g => g.Card != null)
                .OrderBy(g => g.Card.IsEnergy ? 0 : 1)
                .ThenBy(g => g.Card.TotalCost)
                .ThenBy(g => g.Card.Name)
                .ToList();
        }
    }

    private (int Main, int OffColor, int Neutral) ColorCounts => CurrentDeck.GetColorCounts(CardDb.GetCard);
    private void MainWillChanged()
    {
        SavedMessage = null;
        if (SecondWill == CurrentDeck.MainWill) SecondWill = "";
        DropLockedFilter(); ValidateDeck();
    }

    // 單色／雙色 is a builder-only choice (not saved): it follows the existing rule of at most two factions plus neutral,
    // and is re-derived from the cards whenever a deck is opened, so deck files are unchanged.
    private bool DualColor;
    private string SecondWill = "";
    private HashSet<string>? AllowedWills
    {
        get
        {
            if (!Deck.Wills.Contains(CurrentDeck.MainWill)) return null;
            var set = new HashSet<string> { CurrentDeck.MainWill, "中立" };
            if (DualColor) { if (SecondWill != "") set.Add(SecondWill); else set.UnionWith(Deck.Wills); }
            return set;
        }
    }
    private bool LockedOut(string will) => AllowedWills is { } a && !a.Contains(will);
    private string WillChipState(string will) => AllowedWills == null ? "" : LockedOut(will) ? "locked" : will == CurrentDeck.MainWill ? "main-will" : will == "中立" ? "" : "second-will";
    private string WillChipTitle(string will) => LockedOut(will) ? "不在這副牌組的組色內" : will == CurrentDeck.MainWill ? "主色" : will == SecondWill ? "副色" : "";
    private string LockSummary => string.Join(L.IsEnglish ? " + " : "＋", new[] { CurrentDeck.MainWill, DualColor ? (SecondWill == "" ? "任一副色" : SecondWill) : null, "中立" }.Where(x => x != null).Select(x => L[x!]));
    private int OutsideColorCount => AllowedWills is { } a ? CurrentDeck.CardIds.Count(id => CardDb.GetCard(id) is { } c && !a.Contains(c.Will)) : 0;
    private void DropLockedFilter() { if (SelectedWill != "" && LockedOut(SelectedWill)) SelectedWill = ""; }
    private void SetDual(bool dual)
    {
        DualColor = dual;
        if (dual && SecondWill == "") SecondWill = UsedFactions.FirstOrDefault(w => w != CurrentDeck.MainWill) ?? "";
        DropLockedFilter();
    }
    private void SecondWillChanged(ChangeEventArgs e)
    {
        var will = e.Value?.ToString() ?? "";
        SecondWill = Deck.Wills.Contains(will) && will != CurrentDeck.MainWill ? will : "";
        DropLockedFilter();
    }
    private void SyncColorMode()
    {
        var off = UsedFactions.Where(w => w != CurrentDeck.MainWill).ToArray();
        DualColor = off.Length > 0; SecondWill = off.FirstOrDefault() ?? "";
        DropLockedFilter();
    }

    private int MonsterCount => CurrentDeck.CardIds.Count(id => CardDb.GetCard(id)?.IsMonster == true);
    private int SpellCount => CurrentDeck.CardIds.Count(id => CardDb.GetCard(id)?.IsSpell == true);
    private List<string> UnknownCardIds => CurrentDeck.CardIds.Where(id => CardDb.GetCard(id) == null).Distinct().ToList();

    private void RemoveLegacyCards()
    {
        CurrentDeck.CardIds.RemoveAll(id => CardDb.GetCard(id) == null);
        SavedMessage = null;
        ValidateDeck();
    }

    private string[] UsedFactions=>CurrentDeck.CardIds.Select(CardDb.GetCard).Where(c=>c!=null&&c.Will!="中立").Select(c=>c!.Will).Distinct().ToArray();
    private bool CanAddFaction(CardDefinition card)=>card.Will=="中立"||UsedFactions.Contains(card.Will)||UsedFactions.Length<2;
    private bool CanAddCard(CardDefinition card)
    {
        if (CurrentDeck.CardIds.Count >= 50 || !Deck.Wills.Contains(CurrentDeck.MainWill)) return false;
        if (!CanAddFaction(card) || LockedOut(card.Will)) return false;
        int count = CurrentDeck.CardIds.Count(id => id == card.Id);
        return count < 4;
    }

    private void AddCardToDeck(CardDefinition card)
    {
        SavedMessage = null;
        if (!CanAddCard(card)) return;

        CurrentDeck.CardIds.Add(card.Id);
        if (DualColor && SecondWill == "" && card.Will != "中立" && card.Will != CurrentDeck.MainWill) SecondWill = card.Will;
        ValidateDeck();
    }

    private void RemoveCardFromDeck(CardDefinition card)
    {
        SavedMessage = null;
        CurrentDeck.CardIds.Remove(card.Id);
        ValidateDeck();
    }

    private void ClearDeck()
    {
        SavedMessage = null;
        CurrentDeck.CardIds.Clear();
        ValidateDeck();
    }

    private void CreateNewDeck()
    {
        SavedMessage = null;
        CurrentDeck = new Deck
        {
            Id = Guid.NewGuid().ToString(),
            Name = L["新自訂牌組"],
            CardIds = new List<string>()
        };
        SyncColorMode(); ValidateDeck();
    }

    private void ClonePreset(Deck preset)
    {
        SavedMessage = null;
        CurrentDeck = new Deck
        {
            Id = Guid.NewGuid().ToString(),
            Name = L.F("{0} (副本)", L.DeckName(preset)),
            Description = L.DeckDescription(preset),
            MainWill = preset.MainWill,
            CardIds = new List<string>(preset.CardIds)
        };
        SyncColorMode(); ValidateDeck();
    }

    private void LoadCustomDeck(Deck custom)
    {
        SavedMessage = null;
        CurrentDeck = new Deck
        {
            Id = custom.Id,
            Version = custom.Version,
            Name = custom.Name,
            Description = custom.Description,
            MainWill = custom.MainWill,
            CardIds = new List<string>(custom.CardIds)
        };
        SyncColorMode(); ValidateDeck();
    }

    private bool ValidateDeck()
    {
        var valid = CurrentDeck.IsValid(CardDb.GetCard, out var error);
        ValidationErrorMessage = error;
        return valid;
    }

    private void SaveDeck()
    {
        if (string.IsNullOrWhiteSpace(CurrentDeck.Name))
        {
            CurrentDeck.Name = L["未命名自訂牌組"];
        }

        if (!ValidateDeck())
        {
            return;
        }

        try
        {
            DeckSvc.SaveDeck(CurrentDeck);
            ShowSaved($"牌組【{CurrentDeck.Name}】已成功儲存並驗證 50 張。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { SavedMessage = null; ValidationErrorMessage = $"儲存失敗：{ex.Message}"; }
    }

    private void DeleteCurrentDeck() => DeleteCustomDeck(CurrentDeck.Id, CurrentDeck.Version);

    private void DeleteCustomDeck(string id, long version)
    {
        try
        {
            DeckSvc.DeleteDeck(id, version);
            if (CurrentDeck.Id == id) CreateNewDeck();
            ShowSaved("牌組已刪除。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { SavedMessage = null; ValidationErrorMessage = $"刪除失敗：{ex.Message}"; }
    }
}
