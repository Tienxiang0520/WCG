#!/usr/bin/env python3
"""由網站卡牌資料產生根目錄 cards.md（卡牌總表）。

資料來源：WcgWeb/Data/cards.json（中文）與 WcgWeb/i18n/cards.en.json（英文）。
cards.md 不要手改；改卡後執行：
    python3 tools/CardDocs/build_cards_md.py          # 重新產生
    python3 tools/CardDocs/build_cards_md.py --check  # 只檢查是否一致（不一致時結束碼 1）
"""
import json, sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CARDS = ROOT / "WcgWeb" / "Data" / "cards.json"
EN = ROOT / "WcgWeb" / "i18n" / "cards.en.json"
OUT = ROOT / "cards.md"
WILLS = [("狂怒", "Fury"), ("理智", "Reason"), ("生機", "Life"), ("秩序", "Order"), ("深淵", "Abyss"), ("中立", "Neutral")]
TYPE_ORDER = {"怪物": 0, "法術": 1, "法術（反擊）": 2, "結界": 3}
ARROWS = {"up": "↑", "down": "↓", "left": "←", "right": "→"}


def cell(s):
    return str(s).replace("|", "\\|").replace("\n", " ").strip()


def build():
    cards = json.loads(CARDS.read_text(encoding="utf-8"))
    en = json.loads(EN.read_text(encoding="utf-8"))
    types = Counter(c["type"] for c in cards)
    out = [
        "---", "tags:", "  - WCG", "  - 卡牌資料庫", f"total_cards: {len(cards)}",
        "source: WcgWeb/Data/cards.json", "---", "",
        f"# WCG 卡牌總表（共 {len(cards)} 張）", "",
        "> [!NOTE] 自動產生，請勿手改",
        "> 本檔由 `tools/CardDocs/build_cards_md.py` 從 `WcgWeb/Data/cards.json` 與 `WcgWeb/i18n/cards.en.json` 產生。",
        "> 改卡後執行 `python3 tools/CardDocs/build_cards_md.py`；`--check` 可檢查是否同步。",
        "> 規則與名詞（進場、陣亡、犧牲、聖盾、箭頭等）以 `規則.md` 為準。", "",
        "類型：" + "、".join(f"{t} {types[t]} 張" for t in sorted(types, key=lambda t: TYPE_ORDER.get(t, 9))) + "。",
        "白板卡（無效果）在卡面上效果欄留白，此表以「無。」表示。", "",
    ]
    for will, will_en in WILLS:
        group = sorted((c for c in cards if c["will"] == will), key=lambda c: c["id"])
        if not group:
            continue
        out += [f"## {will}（{will_en}，{len(group)} 張）", "",
                "| 編號 | 名稱 | 類型 | 費用 | PP | DP | 箭頭 | 效果 | English |",
                "| --- | --- | --- | ---: | ---: | ---: | --- | --- | --- |"]
        for c in group:
            e = en.get(c["id"], {})
            monster = c["type"] == "怪物"
            arrows = "".join(ARROWS.get(a, a) for a in c.get("arrows") or [])
            english = f"**{e.get('name', '')}** — {e.get('text', '')}" if e else ""
            out.append("| " + " | ".join(cell(x) for x in [
                c["id"], c["name"], c["type"], c["total_cost"],
                c["pp"] if monster else "—", c["dp"] if monster else "—",
                arrows or "—", c["text"], english]) + " |")
        out.append("")
    missing = [c["id"] for c in cards if c["id"] not in en]
    if missing:
        raise SystemExit("cards.en.json 缺少：" + ", ".join(missing))
    return "\n".join(out)


def main():
    text = build()
    if "--check" in sys.argv:
        current = OUT.read_text(encoding="utf-8") if OUT.exists() else ""
        if current != text:
            print("cards.md 與卡牌資料不一致，請執行 python3 tools/CardDocs/build_cards_md.py")
            sys.exit(1)
        print("cards.md 與卡牌資料一致")
        return
    OUT.write_text(text, encoding="utf-8")
    print(f"已產生 cards.md（{text.count(chr(10) + '| WCG-')} 張）")


if __name__ == "__main__":
    main()
