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

    [JsonPropertyName("will")]
    public string Will { get; set; } = ""; // 狂怒, 理智, 生機, 秩序, 深淵, 中立

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
    [JsonIgnore] public bool IsCounter => Type == "法術（反擊）";
    [JsonIgnore] public bool CannotAttack => IsMonster && Id == "WCG-120";
    [JsonIgnore] public bool HasTaunt => Keywords.Contains("嘲諷");
    [JsonIgnore] public bool HasCharge => Keywords.Contains("衝鋒");
    [JsonIgnore] public bool HasDivineShield => Keywords.Contains("聖盾");
    [JsonIgnore] public bool HasTrample => Keywords.Contains("貫穿");
    [JsonIgnore] public bool HasPoison => Keywords.Contains("劇毒");
    [JsonIgnore] public bool HasFreeze => false;
    [JsonIgnore] public bool HasStealth => false;
    [JsonIgnore] public bool HasSilence => IsSpell && Id == "WCG-072";

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
