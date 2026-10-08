#!/usr/bin/env python3
"""產生天梯電腦牌組：WcgWeb/Data/ranked_decks.json。

只使用 cards.json 既有卡牌；每副 50 張、同卡號最多 4 張、最多兩個派系、混色最多 12 張。
低牌位版本會依 downgrades 依序把主題卡換成較笨重的通用卡；大師使用完整牌表。
Variants 是開局時隨機套用的 2 張小幅替換，讓同一副對手每場略有不同。
用法：python3 tools/RankedDecks/build_ranked_decks.py [--check]
"""
import collections, json, pathlib, sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
CARDS = {c["id"]: c for c in json.loads((ROOT / "WcgWeb/Data/cards.json").read_text(encoding="utf-8"))}
OUT = ROOT / "WcgWeb/Data/ranked_decks.json"
TIERS = ["青銅", "白銀", "黃金", "白金", "鑽石", "大師"]
# 每組 2 張：青銅換 8 組（16 張）、白銀 6 組、黃金 4 組、白金 2 組、鑽石 1 組、大師 0 組。
DOWNGRADES_BY_TIER = [8, 6, 4, 2, 1, 0]
# 降級用的中立卡：數值偏弱或需要額外判斷，電腦較難打出價值。依費用挑最接近者。
WEAK_FILLERS = [102, 106, 103, 109, 116, 120]

def W(n): return f"WCG-{n:03d}"

# key, 主色, 名稱, 流派, 首次出現牌位, 說明, 完整牌表, 降級時依序拿掉的卡（電腦最能打出價值的高效怪物與解場）, 隨機微調(out, in, 名稱)
# 首次出現牌位依 RankedCheck 對五副預組的 AI 對照勝率排序：較弱的流派先出現，最強的留到高段。
ARCHETYPES = [
  dict(key="WRATH-BLITZ", will="狂怒", name="燃血突擊", style="快攻", tier=0,
       text="低費怪物與衝鋒搶血，熾紅戰斧和怒意沸騰收尾。",
       cards={3:4,5:4,9:4,181:4,136:4,13:4,182:4,18:2,6:4,14:4,4:4,195:4,101:4},
       down=[9,4,182,136,195,5,181,13],
       var=[(4,2,"致死衝擊"),(101,110,"狼騎兵先遣隊"),(195,124,"血怒祭壇"),(18,17,"滅絕怒火")]),
  dict(key="WRATH-WARBAND", will="狂怒", name="戰鼓狂潮", style="中速", tier=0,
       text="戰鼓手與百夫長堆疊狂怒戰團，靠點殺清出攻擊路線。",
       cards={181:4,182:4,156:4,9:4,5:4,11:2,15:4,16:4,124:2,180:4,17:4,20:2,7:4,2:4},
       down=[156,9,7,2,17,182,15,16],
       var=[(124,195,"熔火爆炸陷阱"),(11,136,"狂戰士斥候"),(20,18,"地獄戰狂"),(15,110,"狼騎兵先遣隊")]),
  dict(key="WRATH-SCORCH", will="狂怒", name="焦土控場", style="控制", tier=4,
       text="狂怒點殺加秩序嘲諷與回復，拖到赤帝與天火浩劫清場。",
       cards={1:4,7:4,2:4,4:4,12:2,17:4,151:2,16:4,20:2,15:2,156:4,170:2,65:4,69:4,66:4},
       down=[17,2,7,69,4,16],
       var=[(12,170,"天火浩劫"),(15,113,"森金持盾衛"),(151,124,"血怒祭壇"),(66,70,"崇高奉獻")]),
  dict(key="WRATH-ASHFALL", will="狂怒", name="灰燼殉爆", style="連動", tier=2,
       text="離場傷害與犧牲直傷互相連動，深淵小怪替狂怒點燃引信。",
       cards={3:4,83:4,85:4,16:4,6:4,13:4,14:4,11:4,88:4,5:4,182:2,195:4,101:4},
       down=[88,16,195,11,6,13],
       var=[(101,110,"狼騎兵先遣隊"),(182,18,"地獄戰狂"),(14,2,"致死衝擊"),(11,136,"狂戰士斥候")]),
  dict(key="REASON-FROST", will="理智", name="霜焰節奏", style="節奏", tier=1,
       text="冰凍、回手與嘲諷聖盾控制節奏，再用時間靜止者打出空檔。",
       cards={23:4,24:4,26:4,137:4,184:4,31:4,35:4,29:2,183:4,196:4,32:4,39:2,104:4,163:2},
       down=[32,104,35,184,137,196],
       var=[(104,102,"精靈遊俠"),(29,33,"秘法竊取者"),(163,40,"星穹領主"),(26,139,"次元回溯")]),
  dict(key="REASON-ARCANE", will="理智", name="奧術控制", style="控制", tier=0,
       text="質能瓦解與魔導樞紐守衛清場，抽牌堆出星穹領主。",
       cards={31:4,32:4,28:4,34:2,39:4,37:4,185:4,40:2,24:4,26:4,196:4,21:4,25:4,113:2},
       down=[32,113,185,26,196,37,31,40],
       var=[(34,173,"維度扭曲"),(113,35,"時間靜止者"),(25,33,"秘法竊取者"),(21,183,"觀星占卜師")]),
  dict(key="REASON-ORACLE", will="理智", name="占卜連鎖", style="連動", tier=0,
       text="大量低費法術與學徒連鎖抽濾，讓符文巨像和樞紐守衛連續觸發。",
       cards={23:4,183:4,130:4,22:4,30:4,166:2,185:4,37:4,24:4,137:4,32:4,125:2,31:4,35:2},
       down=[32,137,35,185,37,31,24,30],
       var=[(125,153,"奧術回音塔"),(30,28,"奧術靈感"),(35,29,"冰霜魔導師"),(166,26,"空間回折")]),
  dict(key="REASON-GLACIER", will="理智", name="冰封堡壘", style="中速", tier=4,
       text="理智冰凍配秩序嘲諷聖盾，穩住場面再逐步推進。",
       cards={31:4,29:4,35:4,26:4,137:4,32:4,33:2,27:2,39:2,24:4,184:4,65:4,69:4,63:4},
       down=[32,35,184,69,137,31],
       var=[(33,21,"秘法學徒"),(27,163,"時空之主"),(39,40,"星穹領主"),(26,196,"維度時空屏障")]),
  dict(key="VITAL-SWARM", will="生機", name="森林鋪場", style="鋪場", tier=0,
       text="孢子、蜂群與喚獸師快速鋪滿五格，用野性萌發強化主力。",
       cards={43:4,47:4,51:4,45:4,187:4,131:4,52:4,42:2,60:2,135:4,49:4,101:4,188:2,197:4},
       down=[49,45,187,131,135,197,51,188],
       var=[(42,56,"生態同調"),(60,162,"世界樹之靈"),(101,102,"精靈遊俠"),(188,57,"遠古守護者"),(52,54,"原始印記")]),
  dict(key="VITAL-BEHEMOTH", will="生機", name="巨獸奔襲", style="巨獸", tier=1,
       text="滋養萌發加速能量，高 PP 巨獸以貫穿和嘲諷碾壓戰線。",
       cards={44:4,126:2,41:4,49:4,146:4,53:4,55:4,57:4,59:2,162:2,172:2,157:4,54:2,197:4,188:4},
       down=[146,53,49,57,55,197],
       var=[(54,186,"母樹共鳴"),(126,50,"甦生之雨"),(172,60,"始祖母樹"),(55,118,"荊棘谷猛虎")]),
  dict(key="VITAL-THORN", will="生機", name="荊棘毒牙", style="中速", tier=5,
       text="劇毒小怪與高 PP 嘲諷互補，深淵裂箭負責點殺。",
       cards={51:4,49:4,146:4,41:4,48:2,57:4,188:2,45:4,53:2,197:4,50:2,132:4,89:4,82:4,55:2},
       down=[82,89,197,146,51,49],
       var=[(50,56,"生態同調"),(48,46,"自然之怒"),(53,59,"蓋亞暴龍"),(55,60,"始祖母樹")]),
  dict(key="VITAL-STAMPEDE", will="生機", name="野性衝鋒", style="快攻", tier=2,
       text="生機低費鋪場搭配狂怒衝鋒，趁對手展開前搶下血量。",
       cards={45:4,51:4,53:4,47:4,101:4,111:4,135:4,187:4,43:4,118:2,5:4,136:4,182:4},
       down=[182,187,135,136,53,45],
       var=[(118,110,"狼騎兵先遣隊"),(101,102,"精靈遊俠"),(47,42,"野性之力"),(111,57,"遠古守護者")]),
  dict(key="ORDER-AEGIS", will="秩序", name="聖盾防線", style="防守", tier=0,
       text="箭頭與附著法術層層給聖盾，靠反擊與嘲諷拖垮攻勢。",
       cards={61:4,63:4,69:4,73:4,147:4,62:4,74:4,189:4,75:2,80:2,129:4,133:4,198:4,171:2},
       down=[129,133,198,80,75,171,61,147],
       var=[(171,179,"阿曼蘇爾"),(62,66,"王者賜福"),(74,70,"崇高奉獻"),(189,72,"淨化聖光"),(80,191,"聖光大審判官")]),
  dict(key="ORDER-RAMPART", will="秩序", name="白銀長城", style="控制", tier=3,
       text="一排嘲諷加旗手與神官回血，最後由阿曼蘇爾補滿生命。",
       cards={129:4,65:4,152:4,69:4,190:4,71:4,66:4,113:4,198:4,191:2,78:2,179:2,68:2,75:2,64:4},
       down=[69,152,129,190,191,198,65,64],
       var=[(68,76,"誓約封禁"),(75,171,"聖光審判巨神"),(71,61,"銀色誓言衛"),(179,80,"弗丁")]),
  dict(key="ORDER-VERDICT", will="秩序", name="誓約審判", style="控制", tier=3,
       text="誓約封禁、聖裁者與枷鎖逐一封鎖威脅，弗丁離場再清場。",
       cards={64:4,76:4,77:4,68:2,78:2,142:4,67:4,191:2,80:2,65:4,69:4,158:4,63:4,72:2,66:4},
       down=[76,142,77,67,64,65],
       var=[(72,189,"聖堂集結號角"),(68,198,"神聖救贖誓約"),(191,79,"日耀聖堂巨像"),(158,152,"禁魔石像")]),
  dict(key="ORDER-GROVE", will="秩序", name="聖林守衛", style="中速", tier=4,
       text="秩序聖盾箭頭保護生機高 PP 嘲諷，一路穩健推進。",
       cards={63:4,61:4,65:4,69:4,73:4,147:2,190:4,75:2,62:4,66:4,49:4,146:4,41:4,158:2},
       down=[73,190,62,146,69,63],
       var=[(147,189,"聖堂集結號角"),(75,171,"聖光審判巨神"),(158,129,"白銀哨衛"),(66,198,"神聖救贖誓約")]),
  dict(key="ABYSS-REVENANT", will="深淵", name="亡者交換", style="墓地", tier=1,
       text="以小怪交換填滿墓地，伏擊魔、希爾瓦與冥王不斷復甦戰力。",
       cards={81:4,83:4,87:4,93:4,97:2,90:4,194:2,175:2,192:4,100:2,132:4,82:4,199:4,148:2,89:4},
       down=[82,89,93,87,199,81],
       var=[(148,193,"噬魂死靈術士"),(175,99,"幽冥巨龍"),(90,86,"衰竭詛咒"),(132,85,"苦痛契約者")]),
  dict(key="ABYSS-ASSASSIN", will="深淵", name="暗影刺殺", style="控制", tier=2,
       text="裂箭、汲取與彗星逐一點殺，幽冥巨龍靠死亡回血。",
       cards={82:4,84:2,86:4,94:4,140:2,89:4,91:4,99:2,100:2,123:2,81:4,87:4,113:4,199:4,159:4},
       down=[94,82,91,86,199,81],
       var=[(84,88,"恐懼吞噬"),(140,92,"絕望疫病"),(123,97,"希爾瓦"),(113,93,"深淵伏擊魔")]),
  dict(key="ABYSS-SOULFEAST", will="深淵", name="噬魂獻祭", style="連動", tier=0,
       text="死靈術士與萬魂之井把犧牲換成手牌，守夜人同步鎖住對手能量。",
       cards={193:4,148:4,83:4,81:4,88:4,96:2,97:2,93:4,87:4,192:4,132:4,194:2,128:2,90:2,199:4},
       down=[87,81,88,93,199,97,194,132],
       var=[(128,169,"暗影獻祭"),(96,94,"靈魂汲取"),(90,100,"瑪爾加尼斯"),(132,85,"苦痛契約者")]),
  dict(key="ABYSS-AMBUSH", will="深淵", name="深淵伏擊", style="快攻", tier=3,
       text="暗影伏擊者鎖能量、狂怒衝鋒與直傷搶血，速度最快的高段對手。",
       cards={138:4,95:2,81:4,85:4,132:4,89:4,87:4,82:4,84:2,83:4,5:4,6:4,14:4,199:2},
       down=[138,82,89,6,87],
       var=[(84,88,"恐懼吞噬"),(95,93,"深淵伏擊魔"),(199,110,"狼騎兵先遣隊"),(85,101,"巡邏輕步兵")]),
]

def counts_to_ids(counts):
    return [W(k) for k in sorted(counts) for _ in range(counts[k])]

def check(deck_will, ids, label):
    errors = []
    if len(ids) != 50: errors.append(f"{len(ids)} 張")
    c = collections.Counter(ids)
    for i, n in c.items():
        if i not in CARDS: errors.append(f"未知卡 {i}")
        elif n > 4: errors.append(f"{i} 超過 4 張")
    wills = {CARDS[i]["will"] for i in ids if i in CARDS and CARDS[i]["will"] != "中立"}
    if len(wills) > 2: errors.append("超過兩派系")
    off = sum(1 for i in ids if i in CARDS and CARDS[i]["will"] not in ("中立", deck_will))
    if off > 12: errors.append(f"混色 {off} 張")
    if errors: raise SystemExit(f"{label} 不合法：" + "、".join(errors))

def swap(counts, out, inn, n=2):
    c = dict(counts)
    if c.get(out, 0) < n: return None
    c[out] -= n
    if c[out] == 0: del c[out]
    c[inn] = c.get(inn, 0) + n
    return c if c[inn] <= 4 else None

def build():
    entries, seen = [], set()
    for tier in range(6):
        for a in ARCHETYPES:
            if a["tier"] > tier: continue
            counts = dict(a["cards"])
            check(a["will"], counts_to_ids(counts), a["name"] + " 完整版")
            # 牌位越低，套用越多組降級替換。
            for step in range(DOWNGRADES_BY_TIER[tier]):
                # 先拿掉指定的主題卡；清單用完後，改拿目前張數最多的非降級卡。
                if step < len(a["down"]): out = a["down"][step]
                else: out = max((k for k in counts if k not in WEAK_FILLERS and counts[k] >= 2), key=lambda k: (counts[k], -k))
                cost = CARDS[W(out)]["total_cost"]
                fillers = sorted(WEAK_FILLERS, key=lambda f: (abs(CARDS[W(f)]["total_cost"] - cost), WEAK_FILLERS.index(f)))
                nxt = next((n for n in (swap(counts, out, f) for f in fillers) if n is not None), None)
                if nxt is None: raise SystemExit(f"{a['name']} 降級 {out} 無法套用")
                counts = nxt
            ids = counts_to_ids(counts)
            label = f"{TIERS[tier]} {a['name']}"
            check(a["will"], ids, label)
            variants = []
            for out, inn, vname in a["var"]:
                v = swap(counts, out, inn)
                if v is None: continue
                check(a["will"], counts_to_ids(v), label + " 微調 " + vname)
                variants.append({"Name": vname, "Out": [W(out)] * 2, "In": [W(inn)] * 2})
            if len(variants) < 3: raise SystemExit(f"{label} 可用微調少於 3 組")
            key = tuple(ids)
            if key in seen: raise SystemExit(f"{label} 與其他牌組重複")
            seen.add(key)
            entries.append({
                "Tier": tier,
                "Archetype": a["style"],
                "Deck": {
                    "Id": f"RANKED-{tier}-{a['key']}",
                    "Name": f"{a['will']}・{a['name']}（{a['style']}）",
                    "Description": a["text"],
                    "MainWill": a["will"],
                    "CardIds": ids,
                },
                "Variants": variants,
            })
    return entries

if __name__ == "__main__":
    entries = build()
    text = json.dumps(entries, ensure_ascii=False, indent=2) + "\n"
    if "--check" in sys.argv:
        current = OUT.read_text(encoding="utf-8")
        if current != text: raise SystemExit("ranked_decks.json 與產生器不一致，請重新執行產生器。")
        print("ranked_decks.json 與產生器一致")
    else:
        OUT.write_text(text, encoding="utf-8")
    per = collections.Counter(e["Tier"] for e in entries)
    print(f"{len(entries)} 副：" + "、".join(f"{TIERS[t]} {per[t]}" for t in range(6)))
