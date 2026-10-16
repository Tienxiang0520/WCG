using System.Text.Json;
using System.Text.Json.Serialization;

namespace WcgWeb.Models;

public class CardDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = ""; // 怪物, 法術

    // "will" is a string for single-will cards (all existing data) or a two-element array for dual-will cards
    // (MTG-style: the deck must have chosen both wills). Arrays are normalised to the fixed will order
    // 狂怒、理智、生機、秩序、深淵 and written back as a plain string when there is only one will.
    [JsonPropertyName("will")]
    [JsonConverter(typeof(WillListConverter))]
    public string[] Wills { get; set; } = [""];

    // Primary will (first in the fixed order). Single-will cards behave exactly as before.
    [JsonIgnore]
    public string Will { get => Wills.Length > 0 ? Wills[0] : ""; set => Wills = [value ?? ""]; } // 狂怒, 理智, 生機, 秩序, 深淵, 中立

    [JsonIgnore] public bool IsDual => Wills.Length == 2;
    [JsonIgnore] public bool IsNeutral => Wills.All(w => w is "中立" or "");
    // Effects that check a will count both wills of a dual card.
    public bool HasWill(string will) => Wills.Contains(will);
    // Non-neutral wills this card requires the deck to have chosen.
    [JsonIgnore] public IEnumerable<string> FactionWills => Wills.Where(w => w is not ("中立" or ""));

    [JsonPropertyName("cost_spec")]
    public string CostSpec { get; set; } = ""; // 保留舊欄位相容性；新版資料一律空白

    [JsonPropertyName("cost_gen")]
    public int CostGen { get; set; } = 0;

    [JsonPropertyName("total_cost")]
    public int TotalCost { get; set; } = 0;

    [JsonPropertyName("pp")]
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int? PP { get; set; }

    [JsonPropertyName("dp")]
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int? DP { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("design_note")]
    public string DesignNote { get; set; } = "";

    [JsonPropertyName("hs_ref")]
    public string HsRef { get; set; } = "";

    [JsonIgnore]
    public bool IsMonster => Type == "怪物";

    [JsonIgnore]
    public bool IsSpell => Type.StartsWith("法術");

    [JsonIgnore]
    public bool IsEnergy => Type == "能量";

    [JsonPropertyName("keywords")] public string[] Keywords { get; set; } = [];
    [JsonPropertyName("arrows")] public string[] Arrows { get; set; } = [];
    [JsonIgnore] public bool IsEnchantment => Type == "結界";
    // Vanilla cards print "無。" / "None." in the data; card faces show an empty text box instead.
    public static bool IsVanillaText(string? text) => (text ?? "").Trim() is "" or "無" or "無。" or "None" or "None.";
    [JsonIgnore] public bool IsVanilla => IsVanillaText(Text);
    [JsonIgnore] public bool IsCounter => Type == "法術（反擊）";
    [JsonIgnore] public bool CannotAttack => IsMonster && Id == "WCG-120";
    [JsonIgnore] public bool HasTaunt => Keywords.Contains("嘲諷");
    [JsonIgnore] public bool HasCharge => Keywords.Contains("衝鋒");
    public static string NormalizeShieldName(string text) => text.Replace("聖盾", "能量護盾", StringComparison.Ordinal).Replace("能量能量護盾", "能量護盾", StringComparison.Ordinal);
    [JsonIgnore] public bool HasEnergyShield => Keywords.Contains("能量護盾") || Keywords.Contains("聖盾"); // Historical catalogue compatibility.
    [JsonIgnore] public bool HasRage => Keywords.Contains("怒擊");
    [JsonIgnore] public bool HasObservation => Keywords.Contains("觀測");
    [JsonIgnore] public bool HasWild => Keywords.Contains("野性");
    [JsonIgnore] public bool HasSoulReturn => Keywords.Contains("回魂");
    [JsonIgnore] public bool HasHeroGuard => Keywords.Contains("護主");
    [JsonIgnore] public bool HasTrample => Keywords.Contains("貫穿");
    [JsonIgnore] public bool HasPoison => Keywords.Contains("劇毒");
    [JsonIgnore] public bool HasStealth => false;
    [JsonIgnore] public bool HasSilence => IsSpell && Id is "WCG-072" or "WCG-206" or "WCG-208";

    public Dictionary<string, int> GetSpecificCost()
    {
        var dict = new Dictionary<string, int>();
        if (string.IsNullOrWhiteSpace(CostSpec)) return dict;

        var parts = CostSpec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var kv = part.Split(':');
            if (kv.Length == 2 && int.TryParse(kv[1], out int count))
            {
                dict[kv[0].Trim()] = count;
            }
        }
        return dict;
    }
}

public class FlexibleIntConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetInt32();
        }
        if (reader.TokenType == JsonTokenType.String)
        {
            string? str = reader.GetString();
            if (int.TryParse(str, out int val))
            {
                return val;
            }
            return null;
        }
        return null;
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteNumberValue(value.Value);
        else
            writer.WriteNullValue();
    }
}

public class WillListConverter : JsonConverter<string[]>
{
    public static readonly string[] Order = ["狂怒", "理智", "生機", "秩序", "深淵"];
    public static string[] Normalize(IEnumerable<string> wills)
    {
        var list = wills.Select(w => (w ?? "").Trim()).Where(w => w != "").Distinct().ToList();
        if (list.Count > 1) list.RemoveAll(w => w == "中立");
        if (list.Count == 0) return [""];
        return list.OrderBy(w => Array.IndexOf(Order, w) is var i && i < 0 ? 99 : i).ToArray();
    }
    public override string[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String) return [reader.GetString() ?? ""];
        if (reader.TokenType == JsonTokenType.Null) return [""];
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("will 必須是字串或字串陣列");
        var list = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            list.Add(reader.TokenType == JsonTokenType.String ? reader.GetString() ?? "" : throw new JsonException("will 陣列只能包含字串"));
        var result = Normalize(list);
        if (result.Length > 2) throw new JsonException("一張卡最多屬於兩個意志");
        return result;
    }
    public override void Write(Utf8JsonWriter writer, string[] value, JsonSerializerOptions options)
    {
        if (value.Length <= 1) { writer.WriteStringValue(value.Length == 0 ? "" : value[0]); return; }
        writer.WriteStartArray(); foreach (var w in value) writer.WriteStringValue(w); writer.WriteEndArray();
    }
}
