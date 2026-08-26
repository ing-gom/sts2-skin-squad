# STS2 Skin Squad

Single-player cosmetic mod for **Slay the Spire 2**. Stands visual-only companions beside you in
combat, laid out exactly where co-op teammates would stand — so a solo run looks like a party run.
Each squad member gets its own look: the vanilla art, an installed skin mod, or another character
entirely.

Status: **v0.13.8** — published on the Steam Workshop
([3774905195](https://steamcommunity.com/sharedfiles/filedetails/?id=3774905195)).

## What it does

- Places 1–3 visual companions alongside the character you control, **in combat, around the
  campfire and in the shop** — the three screens that show the whole party in co-op.
- **Per-member appearance**: `Self`, `Random`, every character's vanilla look, every installed skin
  mod, and any custom character. The list is grouped by character with vanilla first, and each row
  shows its position (`[7/25]`) and a portrait. Hovering a row plays that look's **idle animation**
  in a panel on the left, which is what actually tells near-identical entries apart. Two members of
  the same character can wear two different skins.
- **The squad animates with you** — attack, cast, hurt and death triggers are mirrored onto every
  member (and their pets), so they swing when you swing instead of idling through the fight.
- Uses the game's own co-op geometry: front row at the same height as you, extra members drift up
  and to the left in a grid, back rows greyed out, spacing compressed when the party gets wide.
- Copies your pets — a Necrobinder squad member brings its own Osty rather than sharing yours.
- Disables itself automatically when the run has 2+ players (real co-op, or a fake-multiplayer
  solo-squad mod such as SingleDog / DualRoleAdventure / AI Teammate).

## How the animation stays in step

Every creature animation in the game funnels through `NCreature.SetAnimationTrigger`, which forwards
to a `CreatureAnimator` state machine (`Idle` / `Attack` / `Cast` / `Hit` / `Dead` / `Revive`). Each
member gets its own animator built by the same `GenerateAnimator` the game calls, so triggers blend
the way they do for a real creature rather than being hard-cut animation names — and a member that
is a different character animates with that character's own state machine.

Building an animator is gated on the skeleton actually having `idle_loop`: `CreatureAnimator`'s
constructor seeks into the current track without a null check, so a skin missing that animation
would take the combat down. Without one, the member falls back to a plain looping idle.

The animator is built **after** any skin swap, since it binds to whatever skeleton the sprite holds
at that moment.

## What it deliberately does not do

Squad members are **not characters**. Each one is a bare `NCreatureVisuals` — the same node the game
itself instantiates outside combat — with **no `Creature` entity attached**. That means:

- no health bar, no intents, no orbs, no hitbox, no targeting;
- they never enter `CombatState`, so they cannot affect damage, turn order, or rewards;
- nothing is written to the run or the save.

The combat still sees exactly one player. That is asserted by the automated test, not assumed.

## The three screens

A character is not one skeleton but three, in different scenes with different node scripts, so each
screen needed its own handling — and a skin mod may ship any subset of them.

| Screen | Rig | How the squad gets there |
|---|---|---|
| Combat | `animations/characters/{char}/{char}` | Extra `NCreatureVisuals`, laid out by the game's co-op geometry |
| Campfire | `animations/rest_site/{char}/restsite_{char}` (act-specific loops) | The room owns four fixed slot containers and solo fills only the first, so the squad takes the rest |
| Shop | `animations/merchant/{char}/{char}_shop` | The game's own grid, re-run over the larger party |

The campfire actor is the awkward one. `NRestSiteCharacter.Create` demands a `Player`, and its
`_Ready` dereferences `Player.RunState` and branches on `Player.Character is Necrobinder` — which
would throw here, because a member's character need not match yours (an Ironclad member beside a
Necrobinder player goes looking for `%NecroFire` on the Ironclad scene). Rather than fabricate a
Player, the scene is instantiated directly, a patch skips `_Ready` for these nodes, and the mod does
the Player-independent half itself: resolve the node's own field references, neutralise the hitbox so
a decoration can never take a click, and start the act's loop.

## How multiple skins on one character are possible

The usual way to change a character's look is mounting a `.pck` that overrides
`res://animations/characters/...`. That is global by construction: two skins for one character claim
the same paths and the last mount wins, so "different skins side by side" is impossible that way.

This mod never mounts anything. It reads the skin's `.pck` bytes directly and rebuilds the assets on
disk, then binds them to **one SpineSprite instance**. What makes that cheap is what those archives
actually hold:

| In the pck | Actually is | Rebuilt as |
|---|---|---|
| `*.spskel` | raw Spine 4.2.43 binary, no Godot wrapper | `skin.skel`, byte-for-byte |
| `*.spatlas` | JSON wrapping the original libgdx atlas text in `atlas_data` | `skin.atlas` |
| `*.ctex` | Godot header + embedded PNG/WebP | the `.png` the atlas names |

Extracted skins are cached under the game's **user data** directory, never inside the mod folder —
a Workshop upload packages the mod folder, and a cache there would ship other mods' art.

A raw load skips Godot's importer, so a skeleton authored at a different pixel scale renders at the
wrong size. The mod measures the stock body's skeleton bounds before the swap and rescales to match
(measured in practice: one Necrobinder skin needed ×2.39).

## Setting it up

**Character select screen → `Squad` button.** With Skin Manager installed the button mounts *inside* its menu bar, beside Save / Discard, so the two mods read as one panel and the button follows when that panel is dragged. Without it, the button anchors to the bottom-right near Embark and can be **dragged anywhere**; where
you drop it is remembered. Inside the panel it is deliberately *not* draggable on its own — it is a
row item there, and moves only when that panel is dragged. It opens a picker with one row per
member: squad size at the top, then each member's look with a preview thumbnail and ◀ ▶ to cycle.
This is the intended way to configure the mod — it shows you what you are choosing.

The same values also appear in the game's mod settings (RitsuLib **or** ModConfig, whichever is
installed; no hard dependency on either), which additionally exposes the two layout toggles:

| Setting | Default | Meaning |
|---|---|---|
| Enable squad | on | Master switch |
| Squad members | 2 | How many companions stand beside you (0–3) |
| Member 1/2/3 looks like | Same as me | `Self`, `Random`, a character, or an installed skin |
| Give pets room | on | Push each member's pet clear of its body (off = the game's own overlapping placement for non-local players) |
| Dim back rows | on | Grey out members behind the front row, as the game does for distant teammates |

Settings live in `skin_squad.json` in the game's user data directory, and that file is the single
source of truth. The settings framework can be read through `ModConfigBridge` but not written to, so
the in-game picker would have had nowhere to persist a choice; adding a setter would have meant a
new ModKit API, which is unusable until every other installed consumer is rebuilt (see the build
note below). Both editors therefore write to this file.

## Relationship to Skin Manager

**No dependency, in either direction.** This mod reads other mods' `.pck` files with its own reader
and needs nothing from Skin Manager at load time; it is fully usable on its own, since standing other
*vanilla* characters beside you requires no skin mods at all.

What it does do is integrate when Skin Manager happens to be there: the Squad button moves into that
mod's menu bar, and the picker draws above its panel and hover preview. Every step of that degrades
to the standalone placement if anything is missing, so a Skin Manager update cannot break this mod
beyond moving a button back to the corner.

## Why "vanilla" is an extracted skin too

A mounted skin **replaces** the stock character, so with `ironcladSkin` installed the plain Ironclad
body already renders as that skin. Listing the character and the skin as separate options therefore
put the same figure in two places in the list.

So the game's own `SlayTheSpire2.pck` is scanned like any other archive, and each character's base
art becomes an explicit `vanilla:<character>` entry that swaps the base skeleton in. Picking vanilla
now genuinely gives you the vanilla look, whatever skin mods are mounted. Characters with no entry in
the base archive — custom characters from character mods — are still listed, by id, at the end.

## Testing animations by hand

```
`            open the dev console (also ' ^ * or Shift+8; debug commands are
             enabled automatically whenever any mod is loaded)
squadanim              list the triggers
squadanim die          death
squadanim win          the post-combat relaxed loop
squadanim attack|cast|hurt|idle|revive
```

It drives the **real** character, not the companions: every creature animation funnels through
`NCreature.SetAnimationTrigger` (even `CreatureCmd.TriggerAnim` ends there), which is the hook the
squad listens on — so a member that fails to follow is a real bug, not a test artefact.

One caveat worth knowing before testing victory: **combat never fires `Relaxed` on a creature.** The
animator accepts it, but the victory and shop screens build their own visuals and play
`relaxed_loop` on those directly. So the squad not showing a victory pose after a real win is
expected; `squadanim win` is the only way to see whether a given skin even has that animation.

## Known limitations

- A skin only changes the screens it ships a rig for. Most skin mods have a combat body and nothing
  else, so such a member wears its skin in combat and the stock look at the campfire and shop. The
  catalog logs which rigs each skin has (`name<Combat+RestSite>`).
- The map screen still shows you alone.
- Skin discovery matches a combat skeleton named after a vanilla character
  (`ironclad`, `silent`, `defect`, `watcher`, `regent`, `necrobinder`). Skeletons named anything else
  are ignored, which correctly skips monster and character-select rigs. Mods that ship under their
  own namespace are still found, because the imported filename derives from the source file, not its
  directory — ATA_IronClad and ATA_Silent both show up in practice.
- A skin can only be worn by the character it was authored for; bone names would not line up
  otherwise.

## Game-branch compatibility

One Workshop item ships one payload, so the same DLL has to serve players on `public` and on
`public-beta`. Nothing here may bind to a shape that exists on only one of them.

Two spots are late-bound for that reason, each with the details in its own file:

- `SpineCompat.cs` — `SetAnimation` changed return type in v0.110.0, and `MegaSpineBinding`
  became `IDisposable` (disposing the wrong wrapper destroys live scene objects).
- `AnimatorCompat.cs` — v0.111.0 gave characters a low-health idle, so
  `CharacterModel.GenerateAnimator(MegaSprite)` became `GenerateAnimator(MegaSprite, Creature)`
  and the one-argument form was removed.

> A parameter or return type that drifts does not fail at the call site. It throws
> `MissingMethodException` when the method *holding* the call is JITted, which is outside the
> `try` around the call — so it surfaces somewhere unrelated, or gets swallowed by an outer
> handler and reads as "the mod loads but does nothing". Compiling successfully against one
> branch is not evidence of compatibility with the other; the member references have to be
> checked against both game builds.

## Building

Requires the sibling `Sts2.ModKit` checkout (dev tree) or the pinned `external/Sts2.ModKit`
submodule. `dotnet build` deploys the DLL + manifest into the game's `mods/Sts2SkinSquad/`.

> The pck/ctex readers live in this mod, not in ModKit, on purpose. ModKit resolution is
> **first-wins** across mods: whichever copy of `Sts2.ModKit.dll` loads first serves everyone, so a
> newly added kit API throws `Could not load type` until every other installed consumer is rebuilt.
> That failure was hit during development and is why these two files were moved back in-tree.

## Verification

`Sts2SkinSquadCode/SoloTest.cs` is a solo-verify harness, inert unless `selftest.sp.flag` sits next
to the DLL. It starts a Necrobinder single-player run, forces one squad member to another character
and another to a skin mod, enters a Monster room, waits for the Osty to be summoned, and asserts:

| | Check |
|---|---|
| A | one player in combat and in the run state (no combat-state pollution) |
| B | the configured number of members spawned |
| C | every member stands left of the real character |
| D | one pet copy per member |
| E | every member carries a loaded spine skeleton and is visible |
| F | the "other character" slot really loaded that character's scene |
| G | the skin slot unpacked to disk and survived the skeleton swap |
| H | every member renders within 0.6x–1.6x of the real character's height |
| I | triggering `Attack` on the real character moves every member off `idle_loop` |
| J | the squad appears at the campfire |
| K | the squad appears in the shop |
| L | the Squad button exists on character select and opens the picker |
| M | no squad node sits right of, or is drawn above, the controlled character |

Assert L also pushes a throwaway button 9000 px off-screen and checks it is pulled back, because a
position saved at one resolution can otherwise land somewhere unreachable at another.

```
powershell -ExecutionPolicy Bypass -File .claude/skills/solo-verify/references/solo-selftest.ps1 -Mod Sts2SkinSquad -TimeoutSec 480
```

Assert H exists because A–G all passed while a swapped skin stood at 42% of the vanilla body's
height: the layout asserts only compare x, so nothing else looked at how big anyone actually was. Its
tolerance depends on whether the member is the same character — a skin must match its own
character's height closely, while a different character legitimately differs (Ironclad measured 1.58x
a Necrobinder). A single tight band for both reported that real height difference as a bug.
