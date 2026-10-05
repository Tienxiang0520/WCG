using System.Text.Json;
using System.Text.Json.Serialization;

namespace LcgWeb.Models;

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
    public bool IsSpell => Type == "法術";

    [JsonIgnore]
    public bool IsEnergy => Type == "能量";

    [JsonIgnore]
    public bool CannotAttack => Text.Contains("不能宣告攻擊") || Text.Contains("無法宣告攻擊");

    [JsonIgnore]
    public bool HasTaunt => IsMonster && Text.StartsWith("嘲諷");

    [JsonIgnore]
    public bool HasCharge => IsMonster && Text.StartsWith("衝鋒");

    [JsonIgnore]
    public bool HasDivineShield => IsMonster && Text.Contains("聖盾") && !Text.Contains("獲得聖盾");

    [JsonIgnore]
    public bool HasTrample => IsMonster && Text.StartsWith("貫穿");

    [JsonIgnore]
    public bool HasPoison => IsMonster && Text.StartsWith("劇毒");

    [JsonIgnore]
    public bool HasFreeze => Text.Contains("冰凍");

    [JsonIgnore]
    public bool HasStealth => IsMonster && Text.StartsWith("潛伏");

    [JsonIgnore]
    public bool HasSilence => IsSpell && Text.StartsWith("沉默");

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
