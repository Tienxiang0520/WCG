# Locally bundled sound effects

All audio assets in this directory are licensed under **Creative Commons Zero (CC0 1.0)** and bundled locally:

- Tactile card handling and physical impact samples (`card-*`, `impactWood_heavy_000.ogg`, `impactMetal_heavy_001.ogg`, `tick_001.ogg`): **Kenney / Kenney Vleugels** (CC0 1.0). The turn cue remains the user-selected A audition, `impactWood_heavy_000.ogg`.
- Interface, spell/enchantment, healing, shield, stat, and victory/defeat cues (`ui-*`, `battle-*`, `stinger-*`): **SoulOath custom-synthesized CC0 1.0** dark-fantasy sound effects crafted and auditioned for cohesion.

Cue names, categories (`ui` / `battle`), pitch-variation (`DETUNE`) and playback gains are configured in `../wcg-audio.js` (`../battle-audio.js` is the battle-board adapter).
Original license texts are preserved in `licenses/`. Every file, its source, author, licence and usage is listed in `CREDITS.md`.

Kenney source packs (downloaded 2026-10-07):

- Casino Audio 1.1: https://kenney.nl/assets/casino-audio
- Interface Sounds 1.0: https://kenney.nl/assets/interface-sounds
- Impact Sounds 1.0: https://kenney.nl/assets/impact-sounds

`manifest.json` records every filename, pack, source, licence, SHA-256, size and duration.
No remote asset requests are made during gameplay.
