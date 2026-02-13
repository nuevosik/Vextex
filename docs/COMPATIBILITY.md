# Vextex — Compatibility (for users)

Short compatibility overview for the Steam Workshop description or for players.

---

## Compatibility

- **Combat Extended:** Native support. Load Vextex after CE. Armor and bulk are normalized; CE-specific settings appear in mod options when CE is detected.

- **DLCs:** Full support for **Royalty** (Psycasters keep Eltex/psychic gear), **Ideology** (preferred apparel gets a bonus to avoid mood debuff), and **Biotech** (toxic resistance is prioritized on polluted maps).

- **Modded items:** Automatically detects shields, jump packs, and special materials via **tags** and **stats** (e.g. `BeltDefense`, `BeltDefensePop`, `Utility`). No need to patch individual mods.

- **Other outfit optimizers:** If you use **Outfitted**, **Best Apparel**, or similar, Vextex can detect it and suggests running in **Passive Mode** (“Another mod fully controls outfit AI” in settings). That avoids two mods fighting over the same colonist.

- **Load order:** Place Vextex after any mod that changes apparel or combat stats you want Vextex to respect. No strict requirement for most setups.

---

## Debug (Dev Mode)

With **Dev Mode** on, select a colonist and use the **“Vextex: Debug outfit”** button in the inspect pane. The log will show current worn scores and the best swap candidate with a short breakdown (armor, insulation, quality, psychic bonus if applicable). Use this to see why a pawn chose a given piece.
