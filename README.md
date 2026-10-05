# OuterCraft

**Minecraft, natively inside Outer Wilds.** Place and mine blocks on the planets of the Hearthian solar
system, craft, fly with elytra and fireworks, light a Nether portal into Dark Bramble — with
Minecraft's own textures, models, sounds, HUD, hand and movement, running on Outer Wilds' spherical
gravity and lighting.

No Minecraft instance runs alongside the game. Everything is rebuilt inside Outer Wilds, and every
Minecraft asset (textures, block and item models, recipes, sounds, skin) is read **at runtime from
your own Minecraft Java Edition installation**. None of it ships with the mod.

## Features

- **Blocks on a cube-sphere grid** stuck to each planet: they orbit and spin with it, and Outer Wilds'
  own character controller walks on them. Minecraft-style ambient occlusion and face shading, drawn
  with Outer Wilds' own shader so they sit in its lighting.
- **Shaped blocks straight from the jar's JSON models**: torches, lanterns, stairs, slabs, doors,
  fences, glass panes, ladders, flowers, furnaces, crafting tables, with block states (facing,
  halves, wall torches, double slabs, fence/pane connections).
- **Survival**: break times by hardness and tool, crack overlay, hit sounds and particles, item drops
  you pick up, stack counts, a builder's kit at the start of every loop (the planets themselves
  can't be mined). **Creative** mode too.
- **Inventory and crafting** (Tab) with Minecraft's screens and all the jar's shaped and shapeless
  recipes; 3x3 crafting at a crafting table.
- **Outer Wilds' own movement and controls** (walking, charge jump, jetpack), with Minecraft's view
  bobbing, first-person hand and held items, and footsteps on blocks.
- **Elytra and firework rockets** with Minecraft's flight physics against each planet's pull.
- **Nether portal**: an obsidian frame lit with flint and steel opens onto Dark Bramble.
- **You** in third person (F5) with your own Minecraft skin (slim arms too), Minecraft's animations, held item and elytra.
- **Hit Hearthians** like villagers. Everyone is back next loop.
- **Minecraft deaths**: the red "You died!" screen over every Outer Wilds death, with its own death
  message (*Steve was blown up by the Sun*, *Steve was eaten by an Anglerfish*, *Steve fell out of
  the world*…), repeated in chat when the next loop starts.
- **Advancements** for Outer Wilds milestones: *We Need to Go Deeper* (Dark Bramble), *Hot Tourist
  Destinations* (the Sun), *Sky's the Limit*, *Fishy Business*, *The End?* and more.
- **Hearthians talk like villagers**: a "hmm" on every page of dialogue.
- **Nomai in the Standard Galactic Alphabet**: the translator shows untranslated words in the
  enchanting table's runes (the real words, letter for letter) and decodes them one by one.
- **Beds**: lie down and time runs fast like at a campfire, under Minecraft's sleep darkness.
  *You may not rest now; the Sun is exploding.* (Bed models need Minecraft 26.1 or newer.)
- **The advanced warp core is an End Crystal**, turning and bobbing like on the End's pillars (the
  broken one a dead, still crystal); the small warp cores are an Eye of Ender and an Ender Pearl.
- **C418's music** at the start of every loop, quietly over the Hearthian system's own sounds.
- **Builds reset with the loop**, like everything else in the Hearthian system.

## Requirements

- **Outer Wilds** (Steam / Epic / Xbox) with the **[Outer Wilds Mod Manager](https://outerwildsmods.com/mod-manager/)**.
- **Minecraft: Java Edition 1.21.4 or newer**, launched at least once (so the client jar and its
  sound assets are on disk). Tested with 26.3. Older versions may miss textures or models.

## Install

1. Download `P5INA.OuterCraft.zip` from the [latest release](https://github.com/p5ina/outercraft/releases/latest).
2. In the Mod Manager, use **Install from zip** (or unzip into
   `%AppData%\OuterWildsModManager\OWML\Mods\`).
3. Start the game. The newest Minecraft client jar is found automatically in
   `%AppData%\.minecraft\versions` or Prism Launcher. If it isn't, set **minecraftJar** in the
   mod's settings to the full path of a client jar (e.g. `...\.minecraft\versions\1.21.4\1.21.4.jar`).

## Controls

| Key | Action |
|---|---|
| G | Toggle Minecraft mode / Outer Wilds mode |
| LMB (hold) | Mine / attack |
| RMB | Place, use doors and crafting tables, fireworks, flint and steel |
| MMB | Pick block |
| 1–9 / wheel | Hotbar slot |
| Tab | Inventory (E stays Outer Wilds' interact) |
| Q / Ctrl+Q | Drop one / drop stack |
| Space in mid-air | Start gliding (elytra worn) |
| Space (hold) | Outer Wilds' crouch (Steve sneaks); release to jump |
| F5 | First person → behind → front |
| [ ] | Creative: cycle every item in the held slot |
| Shift / Space | Leave a bed |

Shift and the rest of the jetpack controls are Outer Wilds' own.

## Settings

| Setting | Default | |
|---|---|---|
| minecraftJar | (auto) | Path to a Minecraft client jar |
| startInMinecraftMode | on | Start in Minecraft mode |
| gameMode | survival | `survival` or `creative` |
| guiScale | 0 | Minecraft GUI scale, 0 = auto |
| reach | 5 | Block reach in metres |
| skin | auto | `auto`: your own skin, from the account signed in to the Minecraft launcher or Prism; a Minecraft username for theirs; a path to a .png; or a default skin from the jar (steve, alex, kai, …) |
| playerName | Steve | Your name in death messages (with `skin: auto`, your account's name unless set) |
| blockBrightness | 0.85 | Tone Minecraft's bright textures down to Outer Wilds' palette |
| outerWildsShading | on | Draw blocks with Outer Wilds' own shader (off: OuterCraft's Minecraft shader) |
| musicVolume | 0.3 | Volume of C418's music at the start of a loop (0 = off) |

## Building

```sh
dotnet build OuterCraft.csproj -c Release
```

Builds straight into the Mod Manager's mod folder on Windows (override with `-p:OutputPath=...`).
Game and OWML assemblies come from the `OuterWildsGameLibs` and `OWML` NuGet packages.
`bundle/outercraft.shaders` is a prebuilt Unity 2019.4 asset bundle of the shaders in `UnityShaders/`.
Every push to `main` with a new `version` in `manifest.json` builds the zip and publishes a release (GitHub Actions).

## Legal

OuterCraft is a fan-made mod, not affiliated with or endorsed by Mojang, Microsoft, Mobius Digital or
Annapurna Interactive. Minecraft is a trademark of Mojang Synergies AB; Outer Wilds is a trademark of
Mobius Digital. This repository contains no Minecraft or Outer Wilds code or assets: Minecraft's are
loaded from your own installation at runtime, Outer Wilds' are referenced, never redistributed.

Inspired by [SkyCraft](https://github.com/chasmlol/SkyCraft) and the "Minecraft inside other
games" mods.

Code: MIT, see [LICENSE](LICENSE).
