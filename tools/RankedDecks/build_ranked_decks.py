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
WEAK_FILLERS = [102, 103, 109, 114, 116, 119]

def W(n): return f"WCG-{n:03d}"
def WILLS_OF(i): w = CARDS[i]["will"]; return w if isinstance(w, list) else [w]

ARCHETYPES = [
  dict(key="WRATH-BLITZ", will="狂怒", name="燃血突擊", style="快攻", tiers=(0,1,2,3,4,5),
       text="低費怪物與衝鋒搶血，熾紅戰斧和怒意沸騰收尾。",
       cards={3:4,5:4,9:4,181:4,136:4,13:4,182:4,18:2,6:4,14:4,4:4,195:4,101:4},
       down=[9,4,182,136,195,5,181,13],
       var=[(4,2,"致死衝擊"),(101,110,"狼騎兵先遣隊"),(195,124,"血怒祭壇"),(18,20,"赤帝")]),
  dict(key="WRATH-WARBAND", will="狂怒", name="戰鼓狂潮", style="中速", tiers=(0,1,2,3,4,5),
       text="戰鼓手與百夫長堆疊狂怒戰團，靠點殺清出攻擊路線。",
       cards={181:4,182:4,156:4,9:4,5:4,11:2,1:4,16:4,124:2,180:4,4:4,20:2,7:4,2:4},
       down=[156,9,7,2,4,182,1,16],
       var=[(124,195,"熔火爆炸陷阱"),(11,136,"狂戰士斥候"),(20,18,"地獄戰狂"),(1,110,"狼騎兵先遣隊")]),
  dict(key="WRATH-SCORCH", will="狂怒", name="焦土控場", style="控制", tiers=(2,3,4,5),
       text="狂怒點殺加秩序嘲諷與回復，拖到赤帝清場控局。",
       cards={1:4,7:4,2:4,4:4,8:2,144:4,151:2,16:4,20:2,158:2,156:4,123:2,65:4,69:4,66:4},
       down=[144,2,7,69,4,16],
       var=[(8,123,"終焉彗星"),(158,113,"森金持盾衛"),(151,124,"血怒祭壇"),(66,70,"崇高奉獻")]),
  dict(key="WRATH-ASHFALL", will="狂怒", name="灰燼殉爆", style="連動", tiers=(1,2,3,4,5),
       text="陣亡傷害與犧牲直傷互相連動，深淵小怪替狂怒點燃引信。",
       cards={3:4,83:4,85:4,16:4,6:4,13:4,14:4,11:4,88:4,5:4,182:2,195:4,101:4},
       down=[88,16,195,11,6,13],
       var=[(101,110,"狼騎兵先遣隊"),(182,18,"地獄戰狂"),(14,2,"致死衝擊"),(11,136,"狂戰士斥候")]),
  dict(key="REASON-FROST", will="理智", name="霜焰節奏", style="節奏", tiers=(1,2,3,4,5),
       text="橫置、回手與嘲諷格擋控制節奏，再用時間靜止者打出空檔。",
       cards={24:4,25:2,26:4,29:4,32:4,35:4,137:2,145:4,163:2,185:4,196:4,229:4,256:4,261:4},
       down=[32,35,145,185,196,229],
       var=[(29,252,"星界巨像"),(256,102,"精靈遊俠"),(163,40,"星穹領主"),(26,28,"奧術靈感")]),
  dict(key="REASON-ARCANE", will="理智", name="奧術控制", style="控制", tiers=(0,1,2,3,4,5),
       text="質能瓦解與魔導樞紐守衛清場，抽牌堆出星穹領主。",
       cards={137:4,32:4,28:2,256:2,37:4,185:4,40:2,24:2,26:4,196:4,21:4,25:4,252:2,258:4,261:4},
       down=[32,185,26,196,37,137,40,252],
       var=[(256,35,"時間靜止者"),(25,22,"伺機待發"),(21,183,"觀星占卜師"),(28,38,"逆流風暴")]),
  dict(key="REASON-ORACLE", will="理智", name="占卜連鎖", style="連動", tiers=(0,1,2,3,4,5),
       text="大量低費法術與學徒連鎖抽濾，讓符文巨像和樞紐守衛連續觸發。",
       cards={23:2,25:4,28:2,29:2,32:4,35:4,37:4,137:4,145:4,166:2,185:4,229:4,252:2,256:4,261:4},
       down=[32,137,35,185,37,25,229,256],
       var=[(28,38,"逆流風暴"),(35,333,"奪心術士"),(166,26,"空間回折"),(23,21,"秘法學徒"),(252,40,"星穹領主")]),
  dict(key="REASON-GLACIER", will="理智", name="冰封堡壘", style="中速", tiers=(4,5),
       text="理智橫置配秩序嘲諷格擋，穩住場面再逐步推進。",
       cards={31:4,29:4,35:4,26:4,137:4,32:4,258:2,145:2,256:2,24:4,184:4,65:4,69:4,63:4},
       down=[32,35,184,69,137,31],
       var=[(258,21,"秘法學徒"),(145,163,"時空之主"),(256,40,"星穹領主"),(26,196,"維度時空屏障")]),
  dict(key="VITAL-SWARM", will="生機", name="森林鋪場", style="鋪場", tiers=(0,1,2,3,4,5),
       text="孢子、蜂群與喚獸師快速鋪滿五格，用野性萌發強化主力。",
       cards={43:4,47:4,51:4,45:4,187:4,156:4,54:4,42:2,60:2,135:4,49:4,188:2,197:4,246:4},
       down=[49,45,187,156,135,197,51,188],
       var=[(42,56,"生態同調"),(60,162,"世界樹之靈"),(188,146,"荊棘巨龜"),(54,186,"母樹共鳴")]),
  dict(key="VITAL-BEHEMOTH", will="生機", name="巨獸奔襲", style="巨獸", tiers=(1,2,3,4,5),
       text="滋養萌發加速能量，高 PP 巨獸以貫穿和嘲諷碾壓戰線。",
       cards={44:4,126:2,41:4,49:4,146:4,53:4,276:4,59:2,162:2,172:2,155:4,54:2,197:4,188:4,247:2,248:2},
       down=[146,53,49,276,155,197],
       var=[(54,186,"母樹共鳴"),(126,56,"生態同調"),(172,60,"始祖母樹"),(155,113,"森金持盾衛")]),
  dict(key="VITAL-THORN", will="生機", name="荊棘毒牙", style="中速", tiers=(5,),
       text="劇毒小怪與高 PP 嘲諷互補，深淵裂箭負責點殺。",
       cards={51:4,49:4,146:4,41:4,48:2,276:4,188:2,45:4,53:2,197:4,56:2,132:4,89:4,82:4,155:2},
       down=[82,89,197,146,51,49],
       var=[(56,246,"藤鞭絞殺"),(48,46,"自然之怒"),(53,59,"蓋亞暴龍"),(155,60,"始祖母樹")]),
  dict(key="VITAL-STAMPEDE", will="生機", name="野性衝鋒", style="快攻", tiers=(0,1,2,3,4,5),
       text="生機低費鋪場搭配狂怒衝鋒，趁對手展開前搶下血量。",
       cards={45:4,51:4,53:4,47:4,101:4,110:4,135:4,187:4,43:4,276:2,5:4,136:4,182:4},
       down=[182,187,135,136,53,45],
       var=[(276,49,"鐵木樹人"),(101,102,"精靈遊俠"),(47,42,"野性之力"),(110,146,"荊棘巨龜")]),
  dict(key="ORDER-AEGIS", will="秩序", name="格擋防線", style="防守", tiers=(0,1,2,3,4,5),
       text="箭頭與附著法術層層提供格擋，靠反擊與嘲諷拖垮攻勢。",
       cards={62:2,65:4,70:2,73:4,75:4,77:2,80:2,113:4,147:4,179:2,189:4,198:4,200:4,266:4,292:4},
       down=[73,75,147,189,198,200,266,292],
       var=[(62,66,"王者賜福"),(70,190,"白銀軍團旗手"),(189,72,"淨化聖光"),(80,71,"祈光神官")]),
  dict(key="ORDER-RAMPART", will="秩序", name="白銀長城", style="控制", tiers=(5,),
       text="一排嘲諷加旗手與神官回血，最後由阿曼蘇爾補滿生命。",
       cards={129:4,65:4,152:4,69:4,190:4,71:4,66:4,113:4,198:4,73:2,78:2,179:2,68:2,75:2,64:4},
       down=[69,152,129,190,73,198,65,64],
       var=[(68,76,"誓約封禁"),(75,80,"弗丁"),(71,63,"聖印侍從")]),
  dict(key="ORDER-VERDICT", will="秩序", name="誓約審判", style="控制", tiers=(5,),
       text="誓約封禁、聖裁者與枷鎖逐一封鎖威脅，弗丁陣亡後再清場。",
       cards={64:4,76:4,77:4,68:2,78:2,142:4,200:4,190:2,80:2,65:4,69:4,158:4,63:4,72:2,66:4},
       down=[76,142,77,200,64,65],
       var=[(72,189,"聖堂集結號角"),(68,198,"神聖救贖誓約"),(190,179,"阿曼蘇爾"),(158,152,"禁魔石像")]),
  dict(key="ORDER-GROVE", will="秩序", name="聖林守衛", style="中速", tiers=(4,5),
       text="秩序格擋箭頭保護生機高 PP 嘲諷，一路穩健推進。",
       cards={41:4,49:4,53:4,62:2,65:4,66:4,73:4,75:4,80:2,146:2,147:4,158:2,190:2,218:4,292:4},
       down=[73,75,147,218,66,53],
       var=[(147,189,"聖堂集結號角"),(75,179,"阿曼蘇爾"),(158,113,"森金持盾衛"),(66,198,"神聖救贖誓約")]),
  dict(key="ABYSS-REVENANT", will="深淵", name="亡者交換", style="墓地", tiers=(0,1,2,3,4,5),
       text="以小怪交換填滿墓地，伏擊魔、希爾瓦與瑪爾加尼斯不斷復甦戰力。",
       cards={81:4,83:4,87:4,93:4,97:2,90:4,194:2,249:2,192:4,100:2,132:4,82:4,199:4,148:2,89:4},
       down=[82,89,93,87,199,81],
       var=[(148,193,"噬魂死靈術士"),(249,250,"亡者低語"),(90,94,"靈魂汲取"),(132,85,"苦痛契約者")]),
  dict(key="ABYSS-ASSASSIN", will="深淵", name="暗影刺殺", style="控制", tiers=(4,5),
       text="裂箭、汲取與彗星逐一點殺，瑪爾加尼斯回收戰力收官。",
       cards={82:4,84:2,88:4,94:4,92:2,89:4,91:4,97:2,100:2,123:2,81:4,87:4,113:4,199:4,249:4},
       down=[94,82,91,88,199,81],
       var=[(84,90,"冥界引渡"),(92,98,"凋零降臨"),(123,93,"深淵伏擊魔"),(113,152,"禁魔石像")]),
  dict(key="ABYSS-SOULFEAST", will="深淵", name="噬魂獻祭", style="連動", tiers=(0,1,2,3,4,5),
       text="死靈術士、萬魂之井與嗜血戰狂把犧牲換成手牌，地精與怒意沸騰收尾。",
       cards={193:4,148:4,83:4,81:4,88:4,97:2,93:4,87:4,132:4,194:2,128:2,3:4,6:4,11:4},
       down=[87,81,88,93,11,97,194,132],
       var=[(128,169,"暗影獻祭"),(194,100,"瑪爾加尼斯"),(88,82,"暗影裂箭"),(132,85,"苦痛契約者"),(11,13,"焦土縱火犯")]),
  dict(key="ABYSS-AMBUSH", will="深淵", name="深淵伏擊", style="快攻", tiers=(1,2,3,4,5),
       text="暗影伏擊者鎖能量、狂怒衝鋒與直傷搶血，速度最快的高段對手。",
       cards={138:4,95:2,81:4,85:4,132:4,89:4,87:4,82:4,84:2,83:4,5:4,6:4,14:4,199:2},
       down=[138,82,89,6,87],
       var=[(84,88,"恐懼吞噬"),(95,93,"深淵伏擊魔"),(199,110,"狼騎兵先遣隊"),(85,101,"巡邏輕步兵")]),
  dict(key="WRATH-REKINDLE", will="狂怒", name="復燃連擊", style="連擊", tiers=(1,2,3,4,5),
       text="再起戰吼與逆流法師恢復直立，衝鋒戰團反覆壓低對手生命。",
       cards={5:4,11:4,16:4,136:4,392:4,181:4,182:4,195:4,180:4,145:4,26:4,261:4,18:2},
       down=[392,182,136,145,195,5,181,16],
       var=[(11,9,"阿曼尼狂戰士"),(261,24,"寒霜禁錮"),(18,20,"赤帝"),(180,14,"熾紅戰斧")]),
  dict(key="WRATH-FORGE", will="狂怒", name="熔爐戰線", style="結界", tiers=(0,1,2,3,4,5),
       text="熔火鍛爐清理小怪，血怒祭壇把交戰勝利轉為傷害。",
       cards={1:4,7:4,9:4,144:4,16:4,151:4,124:4,156:4,180:4,195:4,242:2,243:4,4:2,20:2},
       down=[151,9,7,195,156,16,243,144],
       var=[(4,104,"腐蝕軟泥怪"),(124,14,"熾紅戰斧"),(20,18,"地獄戰狂"),(180,2,"致死衝擊")]),
  dict(key="REASON-ECHO", will="理智", name="回音法陣", style="結界", tiers=(1,2,3,4,5),
       text="星軌濾牌循環低費法術，樞紐守衛解場、符文巨像逐步進攻。",
       cards={23:4,25:4,35:4,37:4,183:4,185:4,125:2,22:2,137:4,26:4,166:2,196:4,40:2,258:4,113:2},
       down=[125,185,137,35,25,37,196,40],
       var=[(22,28,"奧術靈感"),(166,24,"寒霜禁錮"),(40,163,"時空之主"),(196,184,"奧術共鳴波")]),
  dict(key="REASON-TIME", will="理智", name="時空突擊", style="連擊", tiers=(2,3,4,5),
       text="回手與橫置製造空檔，逆流法師讓狂怒衝鋒再次直立壓迫。",
       cards={21:4,23:4,25:4,256:4,35:4,37:4,145:4,137:4,261:4,196:4,136:4,182:4,163:2},
       down=[145,182,136,35,137,25,37,256],
       var=[(23,130,"觀星學徒"),(261,26,"空間回折"),(163,40,"星穹領主"),(21,183,"觀星占卜師")]),
  dict(key="VITAL-ROOTS", will="生機", name="根鬚覺醒", style="能量", tiers=(1,2,3,4,5),
       text="滋養與根鬚加速巨獸登場，再用甦生與喚醒撐住戰線。",
       cards={41:4,44:2,47:4,49:4,51:2,53:4,59:4,60:2,126:2,143:2,162:2,167:2,172:4,186:4,197:4,276:4},
       down=[59,49,276,172,197,53,186,47],
       var=[(126,54,"原始印記"),(162,247,"大地之心"),(186,56,"生態同調"),(172,146,"荊棘巨龜")]),
  dict(key="VITAL-RECYCLE", will="生機", name="森靈回流", style="墓地", tiers=(0,1,2,3,4,5),
       text="幼芽回收小怪，巨蛋與喚獸師反覆重建戰線。",
       cards={43:4,45:4,47:4,51:4,54:2,131:4,135:2,143:4,187:4,188:4,197:4,58:4,60:2,49:4},
       down=[143,187,131,135,197,51,45,188],
       var=[(54,42,"野性之力"),(135,53,"奔竄巨犀"),(60,162,"世界樹之靈"),(58,103,"淘金鼠人"),(43,56,"生態同調"),(47,146,"荊棘巨龜")]),
  dict(key="ORDER-EXPEDITION", will="秩序", name="聖光遠征", style="進攻", tiers=(1,2,3,4,5),
       text="格擋箭頭保護狂怒衝鋒，以回復支撐主動交換與進攻。",
       cards={65:4,63:4,69:4,73:4,75:4,147:4,189:4,66:4,198:4,5:4,136:4,182:4,80:2},
       down=[182,136,73,69,198,63,65,75],
       var=[(80,179,"阿曼蘇爾"),(189,62,"保護之手"),(147,129,"白銀哨衛"),(66,70,"崇高奉獻")]),
  dict(key="ORDER-PURIFY", will="秩序", name="淨化壁壘", style="驅散", tiers=(2,3,4,5),
       text="壁壘抵擋法術傷害，破咒者與淨化聖光拆除附著與結界。",
       cards={64:4,65:4,72:2,73:4,75:4,76:2,77:4,80:2,112:4,127:4,147:4,189:4,266:4,292:4},
       down=[73,75,112,127,147,189,266,292],
       var=[(127,66,"王者賜福"),(112,113,"森金持盾衛"),(72,142,"誓言枷鎖"),(80,190,"白銀軍團旗手")]),
  dict(key="ABYSS-PLAGUE", will="深淵", name="瘟疫復甦", style="清場", tiers=(2,3,4,5),
       text="疫病與凋零重置場面，再由陣亡與復甦效果保留後續戰力。",
       cards={81:4,83:4,87:4,89:4,91:4,92:4,93:4,97:4,98:4,249:4,199:4,148:4,100:2},
       down=[98,97,93,89,91,199,87,81],
       var=[(148,85,"苦痛契約者"),(92,82,"暗影裂箭"),(100,194,"冥府復甦領主"),(98,94,"靈魂汲取")]),
  dict(key="ABYSS-LOWLIFE", will="深淵", name="殘血魔潮", style="殘血", tiers=(1,2,3,4,5),
       text="失去生命換取手牌與重壓清場，以瑪爾加尼斯回復和直傷收尾。",
       cards={13:4,14:4,81:4,85:4,87:4,93:4,96:4,100:2,136:4,148:2,165:4,193:4,199:4,242:2},
       down=[193,93,87,136,199,85,81],
       var=[(96,88,"恐懼吞噬"),(100,97,"希爾瓦"),(193,89,"骨鐮刺客"),(199,94,"靈魂汲取")]),
  dict(key="REASON-TIDE", will="理智", name="記憶潮汐", style="磨牌", tiers=(3,4,5),
       text="思緒潰散與墓語竊賊將對手牌庫送墓，配合記憶吞噬者持續磨牌。",
       cards={211:4,212:4,213:4,256:4,226:4,250:2,208:2,25:4,32:4,35:4,24:2,26:2,82:4,81:4,37:2},
       down=[256,213,32,35,226,212],
       var=[(250,28,"奧術靈感"),(208,183,"觀星占卜師"),(37,25,"智謀策士"),(24,89,"骨鐮刺客")]),
  dict(key="VITAL-PHALANX", will="生機", name="方陣列隊", style="站位", tiers=(2,3,4,5),
       text="根鬚哨兵與連根古木排成方陣，嫩根幼苗與前鋒盾衛聯防推進。",
       cards={215:4,216:4,217:2,218:4,355:2,206:2,49:4,146:4,41:4,45:4,65:4,73:4,63:4,135:4},
       down=[218,216,49,73,65,146],
       var=[(217,54,"原始印記"),(355,362,"擠枝新芽"),(206,46,"自然之怒"),(135,62,"保護之手")]),
  dict(key="WRATH-WARRUNE", will="狂怒", name="戰紋巨獸", style="附著", tiers=(4,5),
       text="荊棘甲殼與烈焰戰紋疊在共生巨蜥身上，一隻巨獸撞穿防線。",
       cards={220:4,221:4,222:4,223:4,14:2,5:4,9:4,136:4,181:4,182:4,135:4,53:4,51:2,2:2},
       down=[222,221,182,136,53,9],
       var=[(14,110,"狼騎兵先遣隊"),(51,45,"叢林行者"),(2,4,"熾炎猛擊"),(223,156,"荒原巨槌食人魔")]),
  dict(key="REASON-PROPHECY", will="理智", name="預言之盾", style="反擊", tiers=(2,3,4,5),
       text="手牌留著審判回響與預知閃避，符文巨像靠控場反打取勝。",
       cards={26:4,31:4,32:4,35:4,37:4,65:2,73:2,75:2,147:4,154:2,185:4,225:2,226:2,252:2,256:4,261:4},
       down=[31,32,35,37,147,185],
       var=[(256,29,"冰霜魔導師"),(225,70,"崇高奉獻"),(252,40,"星穹領主"),(154,23,"靈光學徒")]),
  dict(key="WRATH-LONEWOLF", will="狂怒", name="孤狼劍道", style="少怪", tiers=(1,2,3,4,5),
       text="場上只留孤狼劍聖或虛空隱士，用決鬥宣言與法術清出路線。",
       cards={228:4,229:4,230:4,231:2,242:2,18:4,136:4,182:2,8:4,2:4,4:4,24:4,26:4,14:4},
       down=[228,230,18,8,2,24],
       var=[(231,110,"狼騎兵先遣隊"),(242,137,"思維凍結"),(182,156,"荒原巨槌食人魔"),(14,184,"奧術共鳴波")]),
  dict(key="VITAL-ROTCYCLE", will="生機", name="腐生循環", style="犧牲", tiers=(2,3,4,5),
       text="犧牲小怪抽牌填墓地，腐木巨像降費登場，屍花讓循環延續。",
       cards={232:4,233:4,234:4,235:4,249:2,88:2,43:4,45:4,47:4,51:4,101:4,154:4,93:2,187:2,58:2},
       down=[234,235,233,45,51,93],
       var=[(249,97,"希爾瓦"),(88,82,"暗影裂箭"),(58,90,"冥界引渡"),(187,132,"腐肉食腐蛛")]),
  dict(key="ORDER-OATHDUEL", will="秩序", name="誓劍決鬥", style="決鬥", tiers=(5,),
       text="帶格擋的騎士指定敵方怪物決鬥，用交戰當解場打穿嘲諷。",
       cards={265:4,266:4,268:4,271:4,230:2,63:4,69:4,73:4,147:2,189:4,62:2,66:2,76:2,77:2,113:2,64:4},
       down=[268,271,266,265], var=[]),
  dict(key="ABYSS-ROTEROSION", will="深淵", name="腐萎侵蝕", style="減益", tiers=(5,),
       text="用箭頭、附著與全場減益把敵方 PP 壓低或歸零，再用劇毒清場搶攻。",
       cards={284:4,288:4,289:4,382:4,383:4,385:4,386:2,390:2,82:4,85:2,89:4,93:4,138:2,249:2,154:4},
       down=[390,386,288,284], var=[]),
  dict(key="WRATH-LANERAID", will="狂怒", name="斷路突襲", style="突襲", tiers=(5,),
       text="把突襲兵放在對手的空格對面，清掉擋路的怪並以越陣突破防線。",
       cards={319:4,321:4,325:4,326:4,261:4,18:2,3:4,5:4,9:4,101:4,4:4,24:4,26:4},
       down=[326,325,321,319], var=[]),
  dict(key="ORDER-GRAVEOATH", will="秩序", name="墓誓軍團", style="墓誓", tiers=(5,),
       text="自己磨牌、犧牲累積墓地厚度，再靠列隊亡兵與點名打出致命收尾。",
       cards={347:4,352:4,192:4,65:4,81:4,82:4,88:4,169:4,90:4,93:4,194:4,249:4,100:2},
       down=[352,347,194,93], var=[]),
  dict(key="ORDER-BLOODOATH", will="秩序", name="血誓反擊", style="血誓", tiers=(5,),
       text="自傷壓低生命觸發誓言之怒與誓血軍旗手加成，殘血反擊一舉翻盤。",
       cards={366:4,367:4,369:4,371:4,195:2,225:2,198:2,129:4,65:4,2:4,240:4,4:4,64:4,13:4},
       down=[367,371,366,369], var=[]),
  dict(key="REASON-TIDECYCLE", will="理智", name="潮汐輪迴", style="潮汐", tiers=(5,),
       text="將自己的怪回手重置進場效果，搭配輪迴之環持續賺取手牌優勢。",
       cards={373:4,374:4,375:4,378:4,130:4,29:4,145:4,321:4,35:4,104:4,26:4,41:4,45:2},
       down=[378,374,375,373], var=[]),
  dict(key="VITAL-ROTMIRE", will="生機", name="腐沼蔓生", style="腐沼", tiers=(5,),
       text="用 ↑ 箭頭和附著卡把敵怪 PP 壓成 0，最後萬腐之母讓整排腐壞歸零。",
       cards={382:4,383:4,385:4,386:4,390:4,46:4,49:4,47:2,41:4,81:4,246:4,83:4,101:4},
       down=[390,386,385,382], var=[]),
  dict(key="ORDER-HOLYSPRING", will="秩序", name="聖泉滿溢", style="聖泉", tiers=(5,),
       text="汲泉修女與滿溢之禱穩定回復，觸發泉光侍祭為友軍帶來高爆發加成。",
       cards={400:4,401:4,402:4,190:4,66:4,70:4,75:4,71:4,129:4,65:4,62:4,78:2,179:2,198:2},
       down=[401,402,400,75], var=[]),
]

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
