# Blender-modeled Studio assets: remaining work

Concept-driven assets are modeled by committed Blender scripts and scored against their concept turnarounds; see the
Studio README, [Model a concept faithfully in Blender](../../asset-studio/README.md#model-a-concept-faithfully-in-blender).
The two helmets from PR #102 (`objects/headgear/shaman_antler_headdress`, `objects/headgear/dark_knight_winged_helm`)
are the first assets. This plan lists what remains.

## Open items

1. **The modeled helmets in the game.** Helmets and body armor are worn in the game, and the class heroes wear
   KayKit's own heads (`docs/character-classes.md`). Head-slot items for the two Blender helmets, with their
   `HiddenModelParts`, remain.
2. **Fit every class head.** Re-model the shaman cap and the dark knight shell against all four class heads (or
   per-class variants) once item 1 settles which features hide; `ctx.host_bvh` takes any host head.
3. **Iterate both helmets.** Use `item compare --audit`: antler splay and skull proportions on the shaman, ear
   clearance on the dark knight, and colors against both concepts.
4. **Kit characters.** Let kit characters wear modeled helmets. `helmets._original` already refits hat-frame art from
   the KayKit head it sits on to any studio `heads.Head` (KayKit's own hats are worn that way); give modeled helmets
   the same refit so `characters.GARMENTS["helmet"]` can take them.
5. **More hosts.** Hand hosts for weapons and props, body hosts for armor plates bound per bone.
6. **Organic forms.** Sculpt and remesh helpers in `blender/modeling/`, committed sculpt inputs as
   `source.<part>.glb`, and optional reconstruction blockouts as fitting targets.
7. **Retire replaced styles.** Remove procedural helmet styles that a modeled asset replaces once recipes use it.
