#!/usr/bin/env python3
"""Build legal theme-preserving candidates for DeckStrength; never reads player saves."""
import argparse
import collections
import itertools
import json
import pathlib
import runpy

ROOT = pathlib.Path(__file__).resolve().parents[2]
# Tested alternatives address weak curves without reducing a strong archetype on purpose.
CURATED_SWAPS = {
    "WRATH-SCORCH": ({7: 4, 4: 2, 12: 2}, {20: 2, 15: 2, 180: 4}),
    "WRATH-WARBAND": ({7: 2, 2: 2, 180: 2, 124: 2}, {136: 4, 18: 4}),
    "VITAL-ROOTS": ({126: 2, 167: 2, 143: 2, 172: 2}, {45: 4, 55: 4}),
    "VITAL-STAMPEDE": ({111: 4, 135: 4}, {110: 4, 143: 4}),
    "VITAL-SWARM": ({54: 2, 135: 2, 188: 2}, {60: 2, 48: 2, 108: 2}),
    "ORDER-PURIFY": ({127: 4, 152: 2}, {75: 4, 66: 2}),
    "REASON-ARCANE": ({28: 2, 34: 2}, {183: 4}),
    "ABYSS-SOULFEAST": ({132: 2, 128: 2, 194: 2}, {85: 2, 169: 2, 100: 2}),
    "ORDER-AEGIS": ({133: 4, 74: 2}, {65: 4, 75: 2}),
    "REASON-GLACIER": ({33: 2, 27: 2}, {21: 4}),
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", choices=("current", "original"), default="current")
    parser.add_argument("--out", default="output/balance/strength-candidates.json")
    parser.add_argument("--opponents-out", default="output/balance/strength-opponents.json")
    args = parser.parse_args()
    source = runpy.run_path(str(ROOT / "tools/RankedDecks/build_ranked_decks.py"))
    cards = source["CARDS"]
    current = {e["Deck"]["Id"]: e["Deck"] for e in json.loads((ROOT / "WcgWeb/Data/ranked_decks.json").read_text()) if e["Tier"] == 5}
    groups, opponents = [], []
    for theme in source["ARCHETYPES"]:
        deck_id = "RANKED-5-" + theme["key"]
        original = dict(current[deck_id], CardIds=source["counts_to_ids"](theme["cards"]))
        base = original if args.base == "original" else current[deck_id]
        opponents.append({"Tier": 5, "Deck": base, "Archetype": theme["style"], "Variants": []})
        candidates, seen = [], set()

        def add(ids, label):
            count = collections.Counter(ids)
            wills = {cards[k]["will"] for k in ids if cards[k]["will"] != "中立"}
            if len(ids) != 50 or max(count.values()) > 4 or len(wills) > 2 or sum(cards[k]["will"] not in ("中立", base["MainWill"]) for k in ids) > 12:
                return
            key = tuple(sorted(ids))
            if key not in seen:
                seen.add(key)
                candidates.append({"Label": label, "Deck": dict(base, CardIds=list(key))})

        add(base["CardIds"], "原完整牌表" if args.base == "original" else "目前強牌表")
        if theme["key"] in CURATED_SWAPS:
            count = collections.Counter(original["CardIds"])
            removed, added = CURATED_SWAPS[theme["key"]]
            for number, copies in removed.items():
                count[source["W"](number)] -= copies
            for number, copies in added.items():
                count[source["W"](number)] += copies
            if min(count.values()) >= 0:
                add(list(count.elements()), "補強配牌")
        for amount in (1, 2):
            for variants in itertools.combinations(theme["var"], amount):
                count = collections.Counter(base["CardIds"])
                for removed, added, _ in variants:
                    count[source["W"](removed)] -= 2
                    count[source["W"](added)] += 2
                if min(count.values()) >= 0:
                    add(list(count.elements()), "＋".join(v[2] for v in variants))
        groups.append({"Id": deck_id, "Name": base["Name"], "Candidates": candidates})
    for filename, data in ((args.out, groups), (args.opponents_out, opponents)):
        path = ROOT / filename
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n")
    print(f"{len(groups)} themes; {sum(len(g['Candidates']) for g in groups)} candidate builds")


if __name__ == "__main__":
    main()
