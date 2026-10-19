# Female necromancer costume

First female adaptation of the [approved knight armor pattern](../knight_plate/README.md). User-approved female adaptation, including the shaped breastplate, oversized skull buckle and connected shoulder cloth.

The female torso keeps the same shoulder/neck transition, breast/belly/skirt balance, low fitted belt, restrained flare and front-to-back hip panels. The hip panels end below the inner skirt and share their carrying bone with the supporting straps. The breast-only shell follows two distinct breast curves; its scalloped lower edge overlaps the extended upper belly plate. The rest of the belly remains covered by the fitted waist plates. The breast shell has distinct shaping and violet seams colored directly into its surface, without added color meshes. Bone skull ornaments reuse the closed harvested cranium greeble; both intersect their supporting plate/belt and follow its bone through motion. The belt skull is doubled to a deliberately oversized buckle, clipped to its front half with a closed planar back and sunk into the belt.

The [costume recipe](costume.json) is art-only. The [necromancer character recipe](../../characters/mage/necromancer/character.json) uses the female body and this coordinated costume instead of the old corset, separate belt and long robe. Short purple cloth shoulder tubes overlap the breastplate and existing sleeves, bridging the exposed shoulder gaps. They use blended chest/upper-arm skinning rather than rigid plate attachments. Her existing face, hair, crown, sleeves, cuffs, boots and staff are retained. Both use the shared character palette gradients and crease shading.

- [Female dummy fitting sheet](../../catalogs/costumes/necromancer/lineup.png) and [walking/attack preview](../../catalogs/costumes/necromancer/lineup.gif).
The male source knight is a scale/motion reference, not the target female silhouette. Generate that comparison in temporary storage with `armor compare costume_necromancer`.
- [Dressed necromancer fitting sheet](../../catalogs/characters/mage/necromancer/lineup.png) and [walking/attack preview](../../catalogs/characters/mage/necromancer/lineup.gif), rendered with the same Godot environment as the dummy.

Regenerate the wearable bundle and dummy previews:

```sh
./.venv/bin/python asset-studio/studio.py armor export costume_necromancer
```

Regenerate the dressed character GLB and Godot previews:

```sh
./.venv/bin/python asset-studio/studio.py armor character costume_necromancer
```

 Final wearable and previews live in the published costume catalog; candidates and review bundles remain in OS temporary storage.

Validation includes closed topology for every costume piece, physical skull/support contact, the approved hip-panel/hem relationship and fixed relative placement of rigid fittings through walking, running and attack samples. Rendered previews remain the visual review gate.

Final outputs: [wearable GLB](../../catalogs/costumes/necromancer/costume.glb). Use `armor compare costume_necromancer` for temporary review, then `armor publish costume_necromancer` to replace the final entry.
