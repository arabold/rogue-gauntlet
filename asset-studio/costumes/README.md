# Armor style guide

The [Paladin plate costume](knight_plate/README.md) is the user-approved reference pattern for all new armor. Use its current [fitting sheet](../catalogs/costumes/knight_plate/lineup.png), [animated fitting](../catalogs/costumes/knight_plate/lineup.gif) as the construction, proportion and shading baseline. Legacy armor studies are excluded from the final catalog.

- Keep a readable neck and shoulder transition above the breastplate. Fit to our shared character body and reference skeleton; do not compensate for missing anatomy with armor or shifted joints.
- Balance breastplate, close-fitting belly and a restrained skirt flare. Keep the belt seated against the actual armor outline, with a readable buckle and substantial but controlled thickness.
- Make curved hip plates visibly longer than the skirt hem and wrap each one from front to back. Leave small center openings. Actual straps and overlaps support the plates; their rigid attachments must stay aligned through motion.
- Use the character palette gradients, crease shading and broad facets. Color trim on the plate surface. Model real rolled edges, fasteners, damage and raised emblems; reuse our greebles and seat them on the actual exterior rather than a guessed body offset.
- Match scale, camera, lighting and animation timestamps when comparing with source assets. Review front, side, back, walking and attacks; closed topology and a shared skeleton do not by themselves establish fit.
- Keep art-only recipes, one selected reference/prompt in authoring folders. All generated candidates and comparisons go to OS temporary storage outside the repository; publish final fittings with `armor publish`. `armor export` includes the wearable GLB and animated previews. Gameplay stats and resource composition belong to a separate step.

This is a shared quality and construction pattern, not a requirement to repeat the Paladin's colors, sunburst emblem or plate material on every armor family. Adapt the forms and material details for mail, leather, bone and other designs while preserving fit, readability and coherent shading.

## Plate family

| Costume | Direction | Review |
| --- | --- | --- |
| [Paladin](knight_plate/README.md) | Approved steel/gold sunburst reference; original knight_plate path retained | Approved reference |
| [Necromancer](necromancer/README.md) | Approved female shaping, fitted belly, skull relief and shoulder cloth | Approved |
| [Practical knight](knight/README.md) | Plain steel, restrained wear and no gold or chest ornament | Approved |
| [Maiden](maiden/README.md) | Female steel cuirass, blue gem and cloth shoulder connections | Approved |
| [Dark knight](dark_knight/README.md) | Broader faceted chest, deeper keel, red gem and seated spikes | Approved |

See the [current plate-family comparison](../catalogs/costumes/plate_family.png) and individual current animated fittings. Build these through the common costume constructor rather than separate copies of the approved recipe's geometry. New fittings use ray hits on the triangulated plate; the analytical surface alone can leave details floating near tapered facets. Tests union the original indexed islands individually to establish actual support contact, in addition to checking closed topology and rigged motion.

This pass covers torso, waist, belt, hip protection and cloth shoulder connections. The five steel costumes now include gorget-supported articulated pauldrons. Boots and gloves remain separate equipment work. Legacy character assemblies still need migration and combined fitting review; generating a replacement study does not automatically update every character using older armor.

## Segmented, leather and padded armor

The next construction studies are [segmented plate](segmented/README.md), [brigandine](brigandine/README.md) and [gambeson](gambeson/README.md), approved by the user. Compare their [current fitting sheet](../catalogs/costumes/field_family.png).

Segmented plate uses real overlapping bands, with all upper bands and rivets attached to the chest. Brigandine uses a close leather shell, an integrated front closure and rows of seated iron rivets. Gambeson uses continuous, softly skinned padded torso shells, diamond stitching on the same surface and quilted cloth skirt/hip panels, with no metal plate overlays. The shared fitted lower construction supplies the belt and suspension proportions; material-specific construction replaces the corresponding plate parts.

## Moving overlaps

Keep the original spine attachment for the upper belly. Binding it to the chest carries its lower portion through the hips-mounted belt during walking. Instead, shorten the upper waist edge (0.02 rig units at the regular front, 0.005 at the back and 0.015 at the curved female front) and recess its upper lip only where the outer plate already covers it, retaining the visible belly and belt cross-section. Lower skirt plates, belt, hip laps and suspension straps stay on the hips.

Check chest coverage and belt clearance together in rendered motion. The previous chest-occlusion-only test missed a belt collision; the regression now samples actual waist surfaces behind both sides of the belt at 25 walking timestamps and requires positive clearance. This is a walking-fit check, not complete collision certification across every animation.

For the soft gambeson, keep the rigid skirt's upper edge inside the belt's height, with a margin below the strap top; both follow the hips. The softly skinned torso extends behind that overlap. Refresh all authored costume exports and previews after shared fitting changes, including the dressed necromancer.

For segmented plate and brigandine, fit the underlap against their actual lower torso bands after replacement, rather than against the knight scaffold. This retains the spine motion and belt clearance while providing space beneath those bands.

## Mail, Crusader and silver scale

[Chain mail](chain_mail/README.md), [Crusader](crusader/README.md) and [scale armor](scale/README.md) are current review studies. See the [family comparison](../catalogs/costumes/mail_family.png). The approved barbarian and royal orc concepts are documented below.

Mail uses broad staggered wire-ring relief fused into continuous dark backing, with actual hollow ring geometry before attachment. The backing provides readable shadow through the ring centers; this is a game-ready relief treatment, not a loose physically simulated chain fabric. Crusader shares that foundation, omits rings covered by its cloth tabard and paints its cross into the cloth surface. Its belt fits the completed tabard/mail stack.

Scale armor reuses `prefabs/details/heraldry.scale_plate`, the actual pointed ridge greeble used by the dragon scale shield, with restrained silver tones and the steel plate material response. Torso scale relief is fused into dark backing and clipped at its borders. Skirt and hip panels are plain metal without scales. A fitted steel gorget is included as a trial; the shield remains green. Continuous torso backings follow the body's skin weights; the belt, skirt and supported hip laps follow the hips. Keep relief clear of the belt overlap.

## Traveling clothes and reusable capes

Ranger, scout, arcanist and adventurer now use a single complete cape from `prefabs/outfits/capes.py`. One continuous closed cloth surface runs from the front chest bib over the shoulders into the back drape. There is no separate collar/gorget component attached to it. The garment fits over the completed armor, preserves the open throat, arches over sleeve roots and follows the chest. It has no physical cloth simulation yet. Current replacements are external review candidates; existing published catalog outputs show the previously approved version until explicit publication.

A gorget and a cape are alternative garment choices. Keep steel gorgets as separate metal neck armor; reject an assembly that includes both. The cape's front yoke belongs to its own smooth cloth shell, with colors and hems on that surface. The blue barbarian uses the same construction in its pelt palette, and the Crusader uses the full-front variant over its existing mail. Travelers retain their own tunics, closures, hems, utility pouches and cape lengths.

`neckwear.py` still provides independent scarves/cowls and metal gorgets where those are explicitly wanted. Do not combine a separate collar with a cape to approximate its shoulder section. Capes remain independently removable over existing torso armor; author their coverage conservatively rather than deleting skin merely because a moving cape is in front of it.

The [steel gorget study](gorget_study/README.md) is a separate fitting on the practical knight, exposed as an independent `kind: gorget` garment. Existing approved armor recipes remain unchanged. The steel collar fits by direct radial hits on the actual chest/back plates, excluding sleeves and avoiding convex hulls across the neck opening. Review the collar's open throat, shaped bib, head clearance and chest overlap before choosing which metal costumes should adopt it.

Validation includes closed topology and triangle budgets, actual contact unions for cape/tunic and belt/pouch, plus open-throat checks for the gorget. These are construction checks; current Godot walking/attack previews provide the motion review, without claiming full collision certification.

Cloth collars and the steel gorget arch over the shoulder sleeve roots before descending onto chest/back. The arch uses the sleeve's upper envelope with a smooth underside constraint, preserving shell thickness instead of hard-clamping vertices flat. Regression rays compare the actual collar underside against the actual shoulder meshes on both sides; a flat torso cross-section alone is insufficient for this fit.


## Gorget-mounted steel shoulders

Paladin, practical knight, Maiden, Dark Knight and segmented steel recipes include the reusable `steel_shoulders` garment. The Maiden sets `gorget: false`: fitted leather shoulder straps attach its caps directly to the breast/back plates, leaving the neckline open. Its back uses the same integrated horizontal steel tones and seams as the belly. The gorget is fitted over the actual chest/back plates, then each mount is seated by a vertical surface hit at a fraction of the gorget width. This supports the narrower female shell as well as the broader metal costumes. The front bib shortens where needed to keep the existing chest gems visible. Caps reuse the existing thick dome geometry; trim colors its lower edge, and a broad lug connects the dome to a pivot hub. Every cap/lug/hub is a single connected closed solid. Fixed mounting pads share the gorget's chest attachment.

The two shoulder-cap joints rotate by half the upper arm's rotation relative to the chest, about the actual mounting points. Each shell stays rigid and its pivot remains fixed to the gorget. `core/hinges.py` appends these joints without changing existing joint indices and bakes their rotations into every source animation at 30 Hz plus the original keys. Wearables with shoulders have 43 joints and retain the 76 source clips; no runtime constraints or special Godot scripts are required. Other costumes retain their existing rigs.

Tests prove actual guard/mount/cap contact, rigid shape and fixed pivots through walking, running and attacks, and agreement between the authored motion and exported GLB. Current Godot fitting and animated comparisons remain the visual review; these checks do not certify every possible arm/helmet/accessory combination against collision.

The Crusader now adds an ivory cape with crimson edging, plus rigid steel pauldrons over the shoulder cloth. Fitted leather shoulder straps seat on the actual tabard/mail supports without a neck gorget. The Crusader is a full-front tabard-cape: its whole chest-to-hem front and rear drape are one continuous cloth surface, with no separate neck bib or front tabard. The cross is painted into that surface. It follows torso/hips skin weights under the fitted belt, and the 43-joint wearable retains baked shoulder motion in all 76 source clips. Concealed back rings are omitted to remain within the 25,000-triangle wearable budget.

Maiden and Crusader use `leather_straps: true` on `steel_shoulders`. Thin closed bands follow outward ray hits on the actual shoulder clothing, with a small embedded margin rather than a raised metal arch. The pivots follow the fitted strap crown, and short leather tongues connect them to the steel caps. The caps keep their rigid baked animation. Tests check leather support material, actual front/back contact and crown-to-shoulder proximity.

Inspect the current [Maiden](../catalogs/costumes/maiden/lineup.png) and [Crusader](../catalogs/costumes/crusader/lineup.png) fittings.

## Barbarian and royal orc directions

The existing blue [barbarian](barbarian/README.md) is preserved as a separate coordinated costume. The new [primitive barbarian](barbarian_primitive/README.md) and [orc king](orc_king/README.md) follow the user-approved concept sheets saved beside their recipes. The primitive set leaves the chest exposed and adds one leather/fur shoulder. The king uses brute-fitted iron plates, mismatched shoulders, socketed tusks, a horned-skull buckle and continuous fur collar. All three are model-review candidates; final catalogs remain reserved for approved outputs. Other orc roles can reuse the new fittings in a later pass.

## Shared belt and fur construction

Use `belts.build_fitted(torso, supports, y, ...)` for new belts. It follows radial hits on the actual completed armor triangles at each height and samples intermediate rows through the strap height, including a wider front. Select the supporting material with `fit_slot` when needed; tuck decorative harness tails beneath the strap instead of letting them determine the whole waist outline. In character recipes use `kind: belt`, `conform: true` and an optional `fit_slot`. The older fixed/envelope hosts remain compatibility tools for existing authored sets, not the default construction method for new belts. Review gap and clipping together around the circumference and through animation.

Use `prefabs.details.fur.border(support, path, outward, ...)` on any closed armor part. Author its continuous root loop in the actual support surface, with local outward and tuft-direction vectors. The helper makes a continuous uneven pelt backing and unions it into that part, preserving palette regions and the carrying bone; disconnected fur and mixed bone attachments are rejected. For multiple borders, create `fur.edge(...)` pieces and call `fur.attach(support, *borders)` together. The belt's upper/lower fur uses these same helpers. Never approximate a border with independent glued-on triangular meshes. Do not union parts carried by different bones.

These helpers are discoverable through `studio.py schema`: `outfits.fitted_belt`, `details.fur`, and `outfits.cape`. Existing accepted costumes are migrated only with renewed visual review; helper extraction alone does not authorize publishing changed models.

## Body visibility beneath equipment

Use a full body to author and fit clothing, but retain only visible skin in the assembled render. `characters.hide_covered` already removes body triangles conservatively under sufficiently enclosing clothing at build time. It samples face centers and near-corners against the covering garment. Partial straps and open capes do not count as skin-covering garments. This is bind-pose coverage processing, not a dynamic collision solver or runtime equipment mask.

For future interchangeable player armor, export or author equipment-specific body-region/triangle masks and apply the combined coverage of equipped pieces when composing the character in Godot. Keep the intact body source and restore skin when a garment is removed. Preserve overlap margins at neck, wrists and hems; bare-chested and cutout outfits must leave their visible skin. Shared skeletons, suitable skin weights, rigid plate attachments and motion reviews are still required at visible boundaries and between overlapping equipment. Runtime masks and gameplay equipment composition belong to the later Godot integration step.
