# Blender-modeled Studio assets: remaining work

Concept-driven assets are modeled by committed Blender scripts and scored against their concept turnarounds; see the
Studio README, [Model a concept faithfully in Blender](../../asset-studio/README.md#model-a-concept-faithfully-in-blender).
The two helmets from PR #102 (`objects/headgear/shaman_antler_headdress`, `objects/headgear/dark_knight_winged_helm`)
are the first assets. This plan lists what remains.

## Open items

1. **Headgear in the game.** Head-slot armor items for the two helmets (`EquipmentSlot.Head`; `BoneAttachmentManager`
   already mounts an armor scene in the class's hat frame), and a per-class decision on what hides under headgear: the
   knight's crown and the class ears show through today (`item validate` → `host_fit`). Verify that `Runtime_Hat`'s
   offset survives under `BoneAttachment3D`.
2. **Fit every class head.** Re-model the shaman cap and the dark knight shell against all four class heads (or
   per-class variants) once item 1 settles which features hide; `ctx.host_bvh` takes any host head.
3. **Iterate both helmets.** Use `item compare --audit`: antler splay and skull proportions on the shaman, ear
   clearance on the dark knight, and colors against both concepts.
4. **Kit characters.** Map hat-frame headgear onto studio `heads.Head` forms (landmark refit), so kit characters can
   wear modeled helmets; `characters.GARMENTS["helmet"]` takes only procedural styles today.
5. **More hosts.** Hand hosts for weapons and props, body hosts for armor plates bound per bone.
6. **Organic forms.** Sculpt and remesh helpers in `blender/modeling/`, committed sculpt inputs as
   `source.<part>.glb`, and optional reconstruction blockouts as fitting targets.
7. **Retire replaced styles.** Remove procedural helmet styles that a modeled asset replaces once recipes use it.
