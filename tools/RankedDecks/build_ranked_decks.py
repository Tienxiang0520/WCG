#!/usr/bin/env python3
"""產生天梯電腦牌組：WcgWeb/Data/ranked_decks.json。

只使用 cards.json 既有卡牌；每副 50 張、同卡號最多 4 張、最多兩個派系（中立不計，副色張數不限）；雙色卡只能放進主色＋副色剛好是它兩個意志的牌組。
每個流派只在 tiers 列出的牌位出現；低牌位版本會依 downgrades 依序把主題卡換成較笨重的通用卡；大師使用完整牌表。
Variants 是開局時隨機套用的 2 張小幅替換，讓同一副對手每場略有不同。
用法：python3 tools/RankedDecks/build_ranked_decks.py [--check]
量測：python3 tools/RankedDecks/build_ranked_decks.py --all-tiers --out /tmp/all.json（每個流派放進每個牌位，供 RankedCheck 比較）
"""
import collections, json, pathlib, sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
CARDS = {c["id"]: c for c in json.loads((ROOT / "WcgWeb/Data/cards.json").read_text(encoding="utf-8"))}
OUT = ROOT / "WcgWeb/Data/ranked_decks.json"
MASTER_BUILD_PATH = ROOT / "WcgWeb/Data/ranked_master_builds.json"
TIERS = ["青銅", "白銀", "黃金", "白金", "鑽石", "大師"]
# 每組 2 張：青銅換 8 組（16 張）、白銀 6 組、黃金 4 組、白金 2 組、鑽石 1 組、大師 0 組。
DOWNGRADES_BY_TIER = [8, 6, 4, 2, 1, 0]
# 降級用的中立卡：數值偏弱或需要額外判斷，電腦較難打出價值。依費用挑最接近者。
WEAK_FILLERS = [102, 106, 103, 109, 116, 120]

def W(n): return f"WCG-{n:03d}"
def WILLS_OF(i): w = CARDS[i]["will"]; return w if isinstance(w, list) else [w]

# key, 主色, 名稱, 流派, 首次出現牌位, 說明, 完整牌表, 降級時依序拿掉的卡（電腦最能打出價值的高效怪物與解場）, 隨機微調(out, in, 名稱)
# 首次出現牌位依 RankedCheck 對五副預組的 AI 對照勝率排序：較弱的流派先出現，最強的留到高段。
ARCHETYPES = [
  dict(key="WRATH-BLITZ", will="狂怒", name="燃血突擊", style="快攻", tiers=(0,1,2,3,4,5),
       text="低費怪物與衝鋒搶血，熾紅戰斧和怒意沸騰收尾。",
       cards={3:4,5:4,9:4,181:4,136:4,13:4,182:4,18:2,6:4,14:4,4:4,195:4,101:4},
       down=[9,4,182,136,195,5,181,13],
       var=[(4,2,"致死衝擊"),(101,110,"狼騎兵先遣隊"),(195,124,"血怒祭壇"),(18,17,"滅絕怒火")]),
  dict(key="WRATH-WARBAND", will="狂怒", name="戰鼓狂潮", style="中速", tiers=(0,1,2,3,4,5),
       text="戰鼓手與百夫長堆疊狂怒戰團，靠點殺清出攻擊路線。",
       cards={181:4,182:4,156:4,9:4,5:4,11:2,15:4,16:4,124:2,180:4,17:4,20:2,7:4,2:4},
       down=[156,9,7,2,17,182,15,16],
       var=[(124,195,"熔火爆炸陷阱"),(11,136,"狂戰士斥候"),(20,18,"地獄戰狂"),(15,110,"狼騎兵先遣隊")]),
  dict(key="WRATH-SCORCH", will="狂怒", name="焦土控場", style="控制", tiers=(2,3,4,5),
       text="狂怒點殺加秩序嘲諷與回復，拖到赤帝與天火浩劫清場。",
       cards={1:4,7:4,2:4,4:4,12:2,17:4,151:2,16:4,20:2,15:2,156:4,170:2,65:4,69:4,66:4},
       down=[17,2,7,69,4,16],
       var=[(12,170,"天火浩劫"),(15,113,"森金持盾衛"),(151,124,"血怒祭壇"),(66,70,"崇高奉獻")]),
  dict(key="WRATH-ASHFALL", will="狂怒", name="灰燼殉爆", style="連動", tiers=(1,2,3,4,5),
       text="陣亡傷害與犧牲直傷互相連動，深淵小怪替狂怒點燃引信。",
       cards={3:4,83:4,85:4,16:4,6:4,13:4,14:4,11:4,88:4,5:4,182:2,195:4,101:4},
       down=[88,16,195,11,6,13],
       var=[(101,110,"狼騎兵先遣隊"),(182,18,"地獄戰狂"),(14,2,"致死衝擊"),(11,136,"狂戰士斥候")]),
  dict(key="REASON-FROST", will="理智", name="霜焰節奏", style="節奏", tiers=(1,2,3,4,5),
       text="橫置、回手與嘲諷聖盾控制節奏，再用時間靜止者打出空檔。",
       cards={23:4,24:4,26:4,137:4,184:4,25:4,35:4,29:2,183:4,196:4,32:4,39:2,104:4,163:2},
       down=[32,104,35,184,137,196],
       var=[(104,102,"精靈遊俠"),(29,33,"秘法竊取者"),(163,40,"星穹領主"),(26,139,"次元回溯")]),
  dict(key="REASON-ARCANE", will="理智", name="奧術控制", style="控制", tiers=(0,1,2,3,4,5),
       text="質能瓦解與魔導樞紐守衛清場，抽牌堆出星穹領主。",
       cards={137:4,32:4,28:2,34:2,39:2,37:4,185:4,40:2,24:2,26:4,196:4,21:4,25:4,113:4,109:4},
       down=[32,113,185,26,196,37,137,40],
       var=[(34,173,"維度扭曲"),(113,35,"時間靜止者"),(25,33,"秘法竊取者"),(21,183,"觀星占卜師")]),
  dict(key="REASON-ORACLE", will="理智", name="占卜連鎖", style="連動", tiers=(0,1,2,3,4,5),
       text="大量低費法術與學徒連鎖抽濾，讓符文巨像和樞紐守衛連續觸發。",
       cards={23:4,183:4,130:2,22:2,30:2,166:2,185:4,37:4,24:2,137:4,32:4,125:2,25:4,35:2,109:4,104:4},
       down=[32,137,35,185,37,25,24,30],
       var=[(125,153,"奧術回音塔"),(30,28,"奧術靈感"),(35,29,"冰霜魔導師"),(166,26,"空間回折"),(130,21,"秘法學徒"),(22,39,"萬象觀測者")]),
  dict(key="REASON-GLACIER", will="理智", name="冰封堡壘", style="中速", tiers=(4,5),
       text="理智橫置配秩序嘲諷聖盾，穩住場面再逐步推進。",
       cards={31:4,29:4,35:4,26:4,137:4,32:4,33:2,145:2,39:2,24:4,184:4,65:4,69:4,63:4},
       down=[32,35,184,69,137,31],
       var=[(33,21,"秘法學徒"),(145,163,"時空之主"),(39,40,"星穹領主"),(26,196,"維度時空屏障")]),
  dict(key="VITAL-SWARM", will="生機", name="森林鋪場", style="鋪場", tiers=(0,1,2,3,4,5),
       text="孢子、蜂群與喚獸師快速鋪滿五格，用野性萌發強化主力。",
       cards={43:4,47:4,51:4,45:4,187:4,156:4,54:4,42:2,60:2,135:4,49:4,101:4,188:2,197:4},
       down=[49,45,187,156,135,197,51,188],
       var=[(42,56,"生態同調"),(60,162,"世界樹之靈"),(101,102,"精靈遊俠"),(188,57,"遠古守護者"),(54,186,"母樹共鳴")]),
  dict(key="VITAL-BEHEMOTH", will="生機", name="巨獸奔襲", style="巨獸", tiers=(1,2,3,4,5),
       text="滋養萌發加速能量，高 PP 巨獸以貫穿和嘲諷碾壓戰線。",
       cards={44:4,126:2,41:4,49:4,146:4,53:4,55:4,57:4,59:2,162:2,172:2,157:4,54:2,197:4,188:4},
       down=[146,53,49,57,55,197],
       var=[(54,186,"母樹共鳴"),(126,50,"甦生之雨"),(172,60,"始祖母樹"),(55,118,"荊棘谷猛虎")]),
  dict(key="VITAL-THORN", will="生機", name="荊棘毒牙", style="中速", tiers=(5,),
       text="劇毒小怪與高 PP 嘲諷互補，深淵裂箭負責點殺。",
       cards={51:4,49:4,146:4,41:4,48:2,57:4,188:2,45:4,53:2,197:4,50:2,132:4,89:4,82:4,55:2},
       down=[82,89,197,146,51,49],
       var=[(50,56,"生態同調"),(48,46,"自然之怒"),(53,59,"蓋亞暴龍"),(55,60,"始祖母樹")]),
  dict(key="VITAL-STAMPEDE", will="生機", name="野性衝鋒", style="快攻", tiers=(0,1,2,3,4,5),
       text="生機低費鋪場搭配狂怒衝鋒，趁對手展開前搶下血量。",
       cards={45:4,51:4,53:4,47:4,101:4,111:4,135:4,187:4,43:4,118:2,5:4,136:4,182:4},
       down=[182,187,135,136,53,45],
       var=[(118,110,"狼騎兵先遣隊"),(101,102,"精靈遊俠"),(47,42,"野性之力"),(111,57,"遠古守護者")]),
  dict(key="ORDER-AEGIS", will="秩序", name="聖盾防線", style="防守", tiers=(0,1,2,3,4,5),
       text="箭頭與附著法術層層給聖盾，靠反擊與嘲諷拖垮攻勢。",
       cards={61:4,63:4,69:4,73:4,147:4,62:4,74:4,189:4,75:2,80:2,129:4,133:4,198:4,171:2},
       down=[129,133,198,80,75,171,61,147],
       var=[(171,179,"阿曼蘇爾"),(62,66,"王者賜福"),(74,70,"崇高奉獻"),(189,72,"淨化聖光"),(80,191,"聖光大審判官")]),
  dict(key="ORDER-RAMPART", will="秩序", name="白銀長城", style="控制", tiers=(5,),
       text="一排嘲諷加旗手與神官回血，最後由阿曼蘇爾補滿生命。",
       cards={129:4,65:4,152:4,69:4,190:4,71:4,66:4,113:4,198:4,191:2,78:2,179:2,68:2,75:2,64:4},
       down=[69,152,129,190,191,198,65,64],
       var=[(68,76,"誓約封禁"),(75,171,"聖光審判巨神"),(71,61,"銀色誓言衛"),(179,80,"弗丁")]),
  dict(key="ORDER-VERDICT", will="秩序", name="誓約審判", style="控制", tiers=(5,),
       text="誓約封禁、聖裁者與枷鎖逐一封鎖威脅，弗丁陣亡後再清場。",
       cards={64:4,76:4,77:4,68:2,78:2,142:4,200:4,191:2,80:2,65:4,69:4,158:4,63:4,72:2,66:4},
       down=[76,142,77,200,64,65],
       var=[(72,189,"聖堂集結號角"),(68,198,"神聖救贖誓約"),(191,79,"日耀聖堂巨像"),(158,152,"禁魔石像")]),
  dict(key="ORDER-GROVE", will="秩序", name="聖林守衛", style="中速", tiers=(4,5),
       text="秩序聖盾箭頭保護生機高 PP 嘲諷，一路穩健推進。",
       cards={63:4,61:4,65:4,69:4,73:4,147:2,190:4,75:2,62:4,66:4,49:4,146:4,41:4,158:2},
       down=[73,190,62,146,69,63],
       var=[(147,189,"聖堂集結號角"),(75,171,"聖光審判巨神"),(158,129,"白銀哨衛"),(66,198,"神聖救贖誓約")]),
  dict(key="ABYSS-REVENANT", will="深淵", name="亡者交換", style="墓地", tiers=(0,1,2,3,4,5),
       text="以小怪交換填滿墓地，伏擊魔、希爾瓦與冥王不斷復甦戰力。",
       cards={81:4,83:4,87:4,93:4,97:2,90:4,194:2,175:2,192:4,100:2,132:4,82:4,199:4,148:2,89:4},
       down=[82,89,93,87,199,81],
       var=[(148,193,"噬魂死靈術士"),(175,99,"幽冥巨龍"),(90,86,"衰竭詛咒"),(132,85,"苦痛契約者")]),
  dict(key="ABYSS-ASSASSIN", will="深淵", name="暗影刺殺", style="控制", tiers=(4,5),
       text="裂箭、汲取與彗星逐一點殺，幽冥巨龍靠死亡回血。",
       cards={82:4,84:2,86:4,94:4,140:2,89:4,91:4,99:2,100:2,123:2,81:4,87:4,113:4,199:4,159:4},
       down=[94,82,91,86,199,81],
       var=[(84,88,"恐懼吞噬"),(140,92,"絕望疫病"),(123,97,"希爾瓦"),(113,93,"深淵伏擊魔")]),
  dict(key="ABYSS-SOULFEAST", will="深淵", name="噬魂獻祭", style="連動", tiers=(0,1,2,3,4,5),
       text="死靈術士、萬魂之井與嗜血戰狂把犧牲換成手牌，地精與怒意沸騰收尾。",
       cards={193:4,148:4,83:4,81:4,88:4,97:2,93:4,87:4,132:4,194:2,128:2,3:4,6:4,11:4},
       down=[87,81,88,93,11,97,194,132],
       var=[(128,169,"暗影獻祭"),(194,99,"幽冥巨龍"),(88,140,"湮滅契約"),(132,85,"苦痛契約者"),(11,13,"焦土縱火犯")]),
  dict(key="ABYSS-AMBUSH", will="深淵", name="深淵伏擊", style="快攻", tiers=(1,2,3,4,5),
       text="暗影伏擊者鎖能量、狂怒衝鋒與直傷搶血，速度最快的高段對手。",
       cards={138:4,95:2,81:4,85:4,132:4,89:4,87:4,82:4,84:2,83:4,5:4,6:4,14:4,199:2},
       down=[138,82,89,6,87],
       var=[(84,88,"恐懼吞噬"),(95,93,"深淵伏擊魔"),(199,110,"狼騎兵先遣隊"),(85,101,"巡邏輕步兵")]),
]

# Additional strategies use existing cards rather than adding another card pool.
ARCHETYPES += [
  dict(key="WRATH-REKINDLE", will="狂怒", name="復燃連擊", style="連擊", tiers=(1,2,3,4,5),
       text="狂亂血宴與逆流法師恢復攻擊，衝鋒戰團反覆壓低對手生命。",
       cards={5:4,11:4,16:4,136:4,141:4,181:4,182:4,195:4,180:4,145:4,26:4,139:4,18:2},
       down=[141,182,136,145,195,5,181,16],
       var=[(11,9,"阿曼尼狂戰士"),(139,24,"寒霜禁錮"),(18,20,"赤帝"),(180,14,"熾紅戰斧")]),
  dict(key="WRATH-FORGE", will="狂怒", name="熔爐戰線", style="結界", tiers=(0,1,2,3,4,5),
       text="熔火鍛爐清理小怪，血怒祭壇把交戰勝利轉為傷害。",
       cards={1:4,7:4,9:4,15:4,16:4,151:4,124:4,156:4,180:4,195:4,113:4,134:4,20:2},
       down=[151,9,7,195,156,16,113,15],
       var=[(134,104,"腐蝕軟泥怪"),(124,14,"熾紅戰斧"),(20,18,"地獄戰狂"),(180,4,"熾炎猛擊")]),
  dict(key="REASON-ECHO", will="理智", name="回音法陣", style="結界", tiers=(1,2,3,4,5),
       text="回音塔循環低費法術，樞紐守衛解場、符文巨像逐步進攻。",
       cards={23:4,25:4,35:4,37:4,183:4,185:4,153:2,22:2,137:4,26:4,166:2,196:4,40:2,109:4,113:2},
       down=[153,185,137,35,25,37,196,40],
       var=[(22,30,"預言秘術"),(166,24,"寒霜禁錮"),(40,163,"時空之主"),(196,184,"奧術共鳴波")]),
  dict(key="REASON-TIME", will="理智", name="時空突擊", style="連擊", tiers=(2,3,4,5),
       text="回手與橫置製造空檔，逆流法師讓狂怒衝鋒再次攻擊。",
       cards={21:4,23:4,25:4,33:4,35:4,37:4,145:4,137:4,139:4,196:4,136:4,182:4,163:2},
       down=[145,182,136,35,137,25,37,33],
       var=[(23,130,"觀星學徒"),(139,26,"空間回折"),(163,40,"星穹領主"),(21,183,"觀星占卜師")]),
  dict(key="VITAL-ROOTS", will="生機", name="根鬚覺醒", style="能量", tiers=(1,2,3,4,5),
       text="滋養與根鬚加速巨獸登場，再用轉直立效果連續進攻。",
       cards={41:4,44:4,49:4,53:4,57:4,59:4,126:4,143:4,167:4,172:4,186:4,197:4,162:2},
       down=[59,143,49,57,172,197,53,186],
       var=[(126,54,"原始印記"),(162,60,"始祖母樹"),(186,56,"生態同調"),(172,157,"蒼翠古樹衛士")]),
  dict(key="VITAL-RECYCLE", will="生機", name="森靈回流", style="墓地", tiers=(0,1,2,3,4,5),
       text="幼芽與殘破魔像回收小怪，巨蛋與喚獸師反覆重建戰線。",
       cards={43:4,45:4,47:4,51:4,54:2,131:4,135:2,143:4,187:4,188:4,197:4,108:4,60:2,49:4},
       down=[143,187,131,135,197,51,45,188],
       var=[(54,42,"野性之力"),(135,55,"沼澤巨鱷"),(60,162,"世界樹之靈"),(108,103,"淘金鼠人"),(43,56,"生態同調"),(47,146,"荊棘巨龜")]),
  dict(key="ORDER-EXPEDITION", will="秩序", name="聖光遠征", style="進攻", tiers=(1,2,3,4,5),
       text="聖盾箭頭保護狂怒衝鋒，以回復支撐主動交換與進攻。",
       cards={61:4,63:4,69:4,73:4,75:4,147:4,189:4,66:4,198:4,5:4,136:4,182:4,80:2},
       down=[182,136,73,69,198,63,61,75],
       var=[(80,171,"聖光審判巨神"),(189,74,"堅壁祈願"),(147,65,"白銀巡邏衛"),(66,70,"崇高奉獻")]),
  dict(key="ORDER-PURIFY", will="秩序", name="淨化壁壘", style="驅散", tiers=(2,3,4,5),
       text="壁壘抵擋法術傷害，驅邪工兵與破咒者拆除附著與結界。",
       cards={63:4,65:4,69:4,127:4,147:4,152:4,189:4,64:4,72:4,76:4,112:4,134:4,191:2},
       down=[76,152,69,64,147,189,134,65],
       var=[(127,66,"王者賜福"),(112,113,"森金持盾衛"),(72,142,"誓言枷鎖"),(191,75,"破邪大天使")]),
  dict(key="ABYSS-PLAGUE", will="深淵", name="瘟疫復甦", style="清場", tiers=(2,3,4,5),
       text="疫病與凋零重置場面，再由陣亡與復甦效果保留後續戰力。",
       cards={81:4,83:4,87:4,89:4,91:4,92:4,93:4,97:4,98:4,99:4,199:4,150:4,100:2},
       down=[98,97,93,89,91,199,87,81],
       var=[(150,148,"墓園守夜人"),(92,82,"暗影裂箭"),(100,175,"冥王"),(98,94,"靈魂汲取")]),
  dict(key="ABYSS-LOWLIFE", will="深淵", name="殘血魔潮", style="殘血", tiers=(1,2,3,4,5),
       text="失去生命換取手牌與熔爐巨怪折扣，以巨龍回復和直傷收尾。",
       cards={81:4,85:4,87:4,93:4,96:4,99:4,160:4,193:4,199:4,13:4,14:4,165:4,100:2},
       down=[160,193,93,87,99,199,85,81],
       var=[(96,88,"恐懼吞噬"),(100,97,"希爾瓦"),(193,148,"墓園守夜人"),(199,94,"靈魂汲取")]),
]

# 2026-10-16 新流派 A–G（設計 v2 第 6 節，PR E）。機制越複雜的流派越晚出現；戰紋巨獸偏強，只在鑽石與大師。
ARCHETYPES += [
  dict(key="REASON-TIDE", will="理智", name="記憶潮汐", style="磨牌", tiers=(3,4,5),
       text="思緒潰散與墓語竊賊把對手牌庫送入墓地，終焉之潮一口氣磨空牌庫。",
       cards={211:4,212:4,213:4,214:4,226:4,250:2,208:2,25:4,32:4,35:4,24:2,26:2,82:4,81:4,37:2},
       down=[214,213,32,35,226,212],
       var=[(250,30,""),(208,33,""),(37,183,""),(24,89,"")]),
  dict(key="VITAL-PHALANX", will="生機", name="方陣列隊", style="站位", tiers=(2,3,4,5),
       text="根鬚哨兵與連根古木排成方陣，前鋒盾衛擋住正對面的敵人。",
       cards={215:4,216:4,217:2,218:4,219:2,206:2,49:4,146:4,41:4,45:4,61:4,73:4,63:4,135:4},
       down=[218,216,49,73,61,146],
       var=[(217,54,""),(219,57,""),(206,46,""),(135,62,"")]),
  dict(key="WRATH-WARRUNE", will="狂怒", name="戰紋巨獸", style="附著", tiers=(4,5),
       text="荊棘甲殼與烈焰戰紋疊在共生巨蜥身上，一隻巨獸撞穿防線。",
       cards={220:4,221:4,222:4,223:4,205:2,5:4,9:4,136:4,181:4,182:4,135:4,53:4,51:2,17:2},
       down=[222,221,182,136,53,9],
       var=[(205,110,""),(51,45,""),(17,2,""),(223,156,"")]),
  dict(key="REASON-PROPHECY", will="理智", name="預言之盾", style="反擊", tiers=(2,3,4,5),
       text="手牌留著審判回響與預知閃避，伺守觀星者靠滿手牌擋在前線。",
       cards={224:4,225:2,226:2,227:4,251:2,31:4,33:4,183:4,37:4,65:4,69:4,256:4,101:4,154:2,66:2},
       down=[224,227,69,31,183,37],
       var=[(251,39,""),(225,70,""),(66,72,""),(154,23,"")]),
  dict(key="WRATH-LONEWOLF", will="狂怒", name="孤狼劍道", style="少怪", tiers=(1,2,3,4,5),
       text="場上只留孤狼劍聖或虛空隱士，用決鬥宣言與法術清出路線。",
       cards={228:4,229:4,230:4,231:2,242:2,18:4,136:4,182:2,17:4,2:4,4:4,24:4,26:4,14:4},
       down=[228,230,18,17,2,24],
       var=[(231,110,""),(242,137,""),(182,156,""),(14,184,"")]),
  dict(key="VITAL-ROTCYCLE", will="生機", name="腐生循環", style="犧牲", tiers=(2,3,4,5),
       text="犧牲小怪抽牌填墓地，腐木巨像降費登場，屍花讓循環延續。",
       cards={232:4,233:4,234:4,235:4,249:2,207:2,43:4,45:4,47:4,51:4,101:4,154:4,93:2,187:2,58:2},
       down=[234,235,233,45,51,93],
       var=[(249,97,""),(207,82,""),(58,90,""),(187,88,"")]),
  dict(key="ORDER-MERCENARY", will="秩序", name="無旗傭兵", style="中立", tiers=(0,1,2,3,4,5),
       text="大量中立傭兵互相加成，傭兵契約找人、流浪將軍壓陣。",
       cards={236:4,237:4,238:4,209:4,210:2,219:2,257:2,110:4,111:4,113:4,114:2,154:4,65:4,69:4,133:2},
       down=[238,236,113,69,111,209],
       var=[(210,118,""),(219,66,""),(257,119,""),(114,155,"")]),
]
# 單色強化卡（241–252）放進單色流派。
def _boost(key, out, adds, down_from=None, down_to=None):
    a = next(x for x in ARCHETYPES if x["key"] == key)
    assert a["cards"].get(out, 0) == sum(adds.values()), key
    del a["cards"][out]
    for k, v in adds.items(): a["cards"][k] = a["cards"].get(k, 0) + v
    a["down"] = [down_to if d == out else d for d in a["down"]]
    a["var"] = [v for v in a["var"] if v[0] != out]
    BOOSTS[key] = (out, adds)
BOOSTS = {}
_boost("WRATH-FORGE", 113, {244: 2, 242: 2}, down_to=244)
_boost("VITAL-SWARM", 101, {246: 4}, down_to=246)
_boost("VITAL-BEHEMOTH", 55, {247: 2, 248: 2}, down_to=248)
_boost("ABYSS-REVENANT", 132, {249: 2, 250: 2}, down_to=249)
_boost("REASON-ARCANE", 113, {251: 2, 252: 2}, down_to=252)

# 2026-10-17 理智加強：新卡 258–262 與改成嘲諷的秘法護衛（251）放進理智流派；占卜連鎖維持 205 前的卡，讓舊卡表的天梯每個牌位仍有理智牌組。大師強牌表做同樣替換（ranked_master_builds.json）。
REASON_SWAPS = {
  "REASON-FROST": [(104, 251, 4), (39, 259, 2)],
  "REASON-ARCANE": [(109, 258, 4), (28, 259, 2), (39, 262, 2)],
  "REASON-ECHO": [(109, 258, 4)],
  "REASON-TIME": [(139, 261, 4)],
  "REASON-TIDE": [(37, 260, 2)],
  "REASON-PROPHECY": [(66, 259, 2), (101, 258, 4)],
}
for _key, _swaps in REASON_SWAPS.items():
    _a = next(x for x in ARCHETYPES if x["key"] == _key)
    for _out, _inn, _n in _swaps:
        assert _a["cards"].get(_out, 0) == _n, (_key, _out)
        del _a["cards"][_out]; _a["cards"][_inn] = _a["cards"].get(_inn, 0) + _n
        _a["down"] = [_inn if d == _out else d for d in _a["down"]]
        _a["var"] = [v for v in _a["var"] if v[0] != _out]

# 2026-10-23 新流派（new-archetypes-plan.md）：先只放大師，勝率不調。
ARCHETYPES += [
  dict(key="ORDER-OATHDUEL", will="秩序", name="誓劍決鬥", style="決鬥", tiers=(5,),
       text="帶聖盾的騎士指定敵方怪物決鬥，用交戰當解場打穿嘲諷。",
       cards={264:4,265:4,266:4,267:4,268:2,269:4,270:2,271:4,272:2,63:4,69:4,61:2,74:2,76:2,77:2,113:2,64:2},
       down=[269,267,266,264], var=[]),
  dict(key="REASON-VERDANTGAP", will="理智", name="翠語夾縫", style="中速", tiers=(5,),
       text="理智與生機的 1300–1700 中堅，卡在多數單解的門檻之間，靠交戰加值與嘲諷穩住場面。",
       cards={273:4,274:4,275:4,276:4,277:4,278:4,279:2,280:4,281:2,282:2,246:4,32:2,154:2,155:2,101:2,102:2,46:2},
       down=[281,278,275,276], var=[]),
  dict(key="ABYSS-ROTEROSION", will="深淵", name="腐萎侵蝕", style="減益", tiers=(5,),
       text="用箭頭、附著和本回合減益把敵方嘲諷縮小，讓中型怪打得贏再打臉。",
       cards={283:4,284:4,285:4,286:4,287:4,288:4,289:2,290:2,291:4,82:4,85:2,89:2,93:2,138:2,159:2,249:2,154:2},
       down=[290,287,286,285], var=[]),
  # 2026-10-23 第二批（new-archetypes-plan-2.md）：先只放大師，勝率不調。
  dict(key="ORDER-BEACONWATCH", will="秩序", name="烽火守望", style="烽火", tiers=(5,),
       text="怪物直立待命，回合結束時造成傷害或回復；嘲諷擋住對手，不攻擊也能贏。",
       cards={292:4,293:4,294:2,295:2,296:2,297:4,298:2,299:2,300:2,65:4,69:4,146:2,246:4,76:2,77:2,49:2,63:2,225:2,68:2},
       down=[300,296,298,293], var=[]),
  dict(key="REASON-ENERGYVAULT", will="理智", name="伏能喚醒", style="伏能", tiers=(5,),
       text="把大怪蓋進能量區，再不付費從能量區叫出來，用大身材打穿嘲諷。",
       cards={301:4,302:4,303:4,304:2,305:2,306:4,307:2,308:2,309:2,40:2,80:2,158:2,256:2,251:2,32:2,76:2,69:2,65:2,24:2,29:2,77:2},
       down=[306,307,303,301], var=[]),
  dict(key="WRATH-ENERGYBURN", will="狂怒", name="焚能爆破", style="焚能", tiers=(5,),
       text="前期快攻打臉，後期把用不到的能量送入墓地，用很低的費用打出直傷收尾。",
       cards={310:4,311:4,312:2,313:4,314:2,315:4,316:4,317:2,318:2,3:4,4:2,5:4,9:4,101:4,181:2,182:2},
       down=[318,312,317,314], var=[]),
  dict(key="VITAL-HOOFCRUSH", will="生機", name="重蹄碾壓", style="碾壓", tiers=(5,),
       text="讓怪物交戰、直立待命或附著來疊 DP 指示物，再越過嘲諷一下打 3～4 點。",
       cards={337:4,338:4,339:4,340:4,341:4,342:2,343:2,344:2,345:4,46:4,246:2,4:2,203:2,135:2,157:2,49:2,167:2,41:2},
       down=[343, 344, 342, 339], var=[]),
  dict(key="WRATH-LANERAID", will="狂怒", name="斷路突襲", style="突襲", tiers=(5,),
       text="把突襲兵放在對手的空格對面，清掉擋路的怪，從空路越過嘲諷打臉。",
       cards={319:4,320:4,321:4,322:2,323:4,324:4,325:2,326:2,327:2,3:4,5:4,9:2,101:4,4:2,24:2,26:2,29:2},
       down=[327, 325, 322, 326], var=[]),
]

# 每個流派只在 tiers 列出的牌位出現（不再自動繼承低牌位流派）。
# 依 2026-10-09 分級電腦模擬（tools/RankedCheck 搭配 --all-tiers）決定：
# 2026-10-10 多樣性調整：抽牌、獻祭與鋪場重新構築後也可進入高牌位；強控制仍維持高段限定。
# 強度矩陣（--matrix）顯示暗影刺殺在黃金、白金遠強於同池流派，改為只在鑽石與大師。
# 黃金的流派原與白銀相同，改讓焦土控場從黃金開始出現。
TIER_GATES = {a["key"]: tuple(a["tiers"]) for a in ARCHETYPES}
for _key, _tiers in TIER_GATES.items():
    if not _tiers or any(t not in range(6) for t in _tiers): raise SystemExit(f"{_key} 牌位設定不正確")

def counts_to_ids(counts):
    return [W(k) for k in sorted(counts) for _ in range(counts[k])]

def check(deck_will, ids, label):
    errors = []
    if len(ids) != 50: errors.append(f"{len(ids)} 張")
    c = collections.Counter(ids)
    for i, n in c.items():
        if i not in CARDS: errors.append(f"未知卡 {i}")
        elif n > 4: errors.append(f"{i} 超過 4 張")
    wills = {w for i in ids if i in CARDS for w in WILLS_OF(i) if w != "中立"}
    if len(wills) > 2: errors.append("超過兩派系")
    for i in set(ids):
        if i in CARDS and len(WILLS_OF(i)) == 2 and (deck_will not in WILLS_OF(i) or not wills <= set(WILLS_OF(i))):
            errors.append(f"雙色卡 {i} 不符主副色")
    if errors: raise SystemExit(f"{label} 不合法：" + "、".join(errors))

# 雙色（兩派系）牌組名稱標出主色＋副色，例如「秩序＋生機」；中立不計。
def deck_wills(will, ids):
    second = sorted({w for i in ids for w in WILLS_OF(i) if w not in ("中立", will)})
    return "＋".join([will] + second)

def swap(counts, out, inn, n=2):
    c = dict(counts)
    if c.get(out, 0) < n: return None
    c[out] -= n
    if c[out] == 0: del c[out]
    c[inn] = c.get(inn, 0) + n
    return c if c[inn] <= 4 else None

def build():
    entries, seen = [], set()
    master_builds = json.loads(MASTER_BUILD_PATH.read_text(encoding="utf-8"))
    if set(master_builds) != {a['key'] for a in ARCHETYPES}:
        raise SystemExit("大師強牌表必須涵蓋全部流派")
    for tier in range(6):
        for a in ARCHETYPES:
            if tier not in TIER_GATES[a["key"]] and not ALL_TIERS: continue
            counts = (collections.Counter(int(i[-3:]) for i in master_builds[a['key']]['CardIds'])
                      if tier == 5 else dict(a["cards"]))
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
            for out, inn, vname in ([] if tier == 5 else a["var"]):
                vname = vname or CARDS[W(inn)]["name"]
                v = swap(counts, out, inn)
                if v is None: continue
                check(a["will"], counts_to_ids(v), label + " 微調 " + vname)
                variants.append({"Name": vname, "Out": [W(out)] * 2, "In": [W(inn)] * 2})
            # Master's measured full builds stay fixed; lower tiers retain their previous simplifications and variety.
            if tier == 5: variants = []
            elif len(variants) < 3: raise SystemExit(f"{label} 可用微調少於 3 組")
            key = tuple(ids)
            if key in seen: raise SystemExit(f"{label} 與其他牌組重複")
            seen.add(key)
            entries.append({
                "Tier": tier,
                "Archetype": a["style"],
                "Deck": {
                    "Id": f"RANKED-{tier}-{a['key']}",
                    "Name": f"{deck_wills(a['will'], ids)}・{a['name']}（{a['style']}）",
                    "Description": a["text"],
                    "MainWill": a["will"],
                    "CardIds": ids,
                },
                "Variants": variants,
            })
    return entries

# 量測用：--all-tiers 讓每個流派出現在每個牌位（各牌位套用該牌位的降級數），輸出到 --out 指定的檔案。
ALL_TIERS = "--all-tiers" in sys.argv

if __name__ == "__main__":
    entries = build()
    text = json.dumps(entries, ensure_ascii=False, indent=2) + "\n"
    if "--check" in sys.argv:
        current = OUT.read_text(encoding="utf-8")
        if current != text: raise SystemExit("ranked_decks.json 與產生器不一致，請重新執行產生器。")
        print("ranked_decks.json 與產生器一致")
    else:
        out = pathlib.Path(sys.argv[sys.argv.index("--out") + 1]) if "--out" in sys.argv else OUT
        if ALL_TIERS and out == OUT: raise SystemExit("--all-tiers 只能搭配 --out 輸出到其他檔案")
        out.write_text(text, encoding="utf-8")
    per = collections.Counter(e["Tier"] for e in entries)
    print(f"{len(entries)} 副：" + "、".join(f"{TIERS[t]} {per[t]}" for t in range(6)))
