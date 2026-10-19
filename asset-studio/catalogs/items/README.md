# Item catalogs

Current catalog: **145 items**, using the shared character shading pipeline.

[Complete visual overview](overview.png) · [Current validation and export status](status.json)

| Category | Items | Catalog |
| --- | ---: | --- |
| Food | 12 | [View food](food.png) |
| Jewelry | 24 | [View jewelry](jewelry.png) |
| Keys | 6 | [View keys](keys.png) |
| Potions | 36 | [View potions](potions.png) |
| Scrolls | 12 | [View scrolls](scrolls.png) |
| Shields | 13 | [View shields](shields.png) |
| Tools | 12 | [View tools](tools.png) |
| Weapons | 30 | [View weapons](weapons.png) |

Each published item has a final GLB, fingerprint manifest and `lineup.png` in its category/name folder: front, three-quarter, side and gameplay-scale views.

Godot-verified current exports: **145/145**. Authored-surface exceptions still flagged for geometry review: **17**. These exceptions are preserved original surfaces, not clean closed-mesh certifications.

Regenerate these catalogs from published outputs with `./.venv/bin/python asset-studio/studio.py item catalog`. Use `item lineup` to render candidates in OS temporary storage, inspect them, then `item publish` to finalize. Rendering never updates this catalog. Changed sources set `source_current: false` until reviewed again.

## Individual lineups

### Food

- [drink_beer_stein](food/drink_beer_stein/lineup.png)
- [drink_water_canteen](food/drink_water_canteen/lineup.png)
- [drink_wine_bottle](food/drink_wine_bottle/lineup.png)
- [food_apple_red](food/food_apple_red/lineup.png)
- [food_bread_loaf](food/food_bread_loaf/lineup.png)
- [food_bread_roll](food/food_bread_roll/lineup.png)
- [food_cheese_wheel](food/food_cheese_wheel/lineup.png)
- [food_grapes_bunch](food/food_grapes_bunch/lineup.png)
- [food_meat_drumstick](food/food_meat_drumstick/lineup.png)
- [food_meat_sausage](food/food_meat_sausage/lineup.png)
- [food_meat_steak](food/food_meat_steak/lineup.png)
- [food_pear_golden](food/food_pear_golden/lineup.png)

### Jewelry

- [amulet_gold_emerald](jewelry/amulet_gold_emerald/lineup.png)
- [amulet_gold_ruby](jewelry/amulet_gold_ruby/lineup.png)
- [amulet_gold_sun_crest](jewelry/amulet_gold_sun_crest/lineup.png)
- [amulet_gold_topaz](jewelry/amulet_gold_topaz/lineup.png)
- [amulet_silver_amethyst](jewelry/amulet_silver_amethyst/lineup.png)
- [amulet_silver_onyx](jewelry/amulet_silver_onyx/lineup.png)
- [amulet_silver_runic_crest](jewelry/amulet_silver_runic_crest/lineup.png)
- [amulet_silver_sapphire](jewelry/amulet_silver_sapphire/lineup.png)
- [amulet_wood_coral](jewelry/amulet_wood_coral/lineup.png)
- [amulet_wood_druid_talisman](jewelry/amulet_wood_druid_talisman/lineup.png)
- [amulet_wood_jade](jewelry/amulet_wood_jade/lineup.png)
- [amulet_wood_peridot](jewelry/amulet_wood_peridot/lineup.png)
- [ring_gold_amethyst](jewelry/ring_gold_amethyst/lineup.png)
- [ring_gold_emerald](jewelry/ring_gold_emerald/lineup.png)
- [ring_gold_ruby](jewelry/ring_gold_ruby/lineup.png)
- [ring_gold_signet](jewelry/ring_gold_signet/lineup.png)
- [ring_silver_band](jewelry/ring_silver_band/lineup.png)
- [ring_silver_diamond](jewelry/ring_silver_diamond/lineup.png)
- [ring_silver_onyx](jewelry/ring_silver_onyx/lineup.png)
- [ring_silver_sapphire](jewelry/ring_silver_sapphire/lineup.png)
- [ring_wood_amber](jewelry/ring_wood_amber/lineup.png)
- [ring_wood_carved](jewelry/ring_wood_carved/lineup.png)
- [ring_wood_garnet](jewelry/ring_wood_garnet/lineup.png)
- [ring_wood_jade](jewelry/ring_wood_jade/lineup.png)

### Keys

- [key_arcane_crystal](keys/key_arcane_crystal/lineup.png)
- [key_bone_skull](keys/key_bone_skull/lineup.png)
- [key_bronze_ornate](keys/key_bronze_ornate/lineup.png)
- [key_gold_treasury](keys/key_gold_treasury/lineup.png)
- [key_iron_prison](keys/key_iron_prison/lineup.png)
- [key_silver_crypt](keys/key_silver_crypt/lineup.png)

### Potions

- [potion_large_cobalt](potions/potion_large_cobalt/lineup.png)
- [potion_large_coral](potions/potion_large_coral/lineup.png)
- [potion_large_crimson](potions/potion_large_crimson/lineup.png)
- [potion_large_emerald](potions/potion_large_emerald/lineup.png)
- [potion_large_gold](potions/potion_large_gold/lineup.png)
- [potion_large_lime](potions/potion_large_lime/lineup.png)
- [potion_large_magenta](potions/potion_large_magenta/lineup.png)
- [potion_large_obsidian](potions/potion_large_obsidian/lineup.png)
- [potion_large_orange](potions/potion_large_orange/lineup.png)
- [potion_large_purple](potions/potion_large_purple/lineup.png)
- [potion_large_silver](potions/potion_large_silver/lineup.png)
- [potion_large_teal](potions/potion_large_teal/lineup.png)
- [potion_medium_cobalt](potions/potion_medium_cobalt/lineup.png)
- [potion_medium_coral](potions/potion_medium_coral/lineup.png)
- [potion_medium_crimson](potions/potion_medium_crimson/lineup.png)
- [potion_medium_emerald](potions/potion_medium_emerald/lineup.png)
- [potion_medium_gold](potions/potion_medium_gold/lineup.png)
- [potion_medium_lime](potions/potion_medium_lime/lineup.png)
- [potion_medium_magenta](potions/potion_medium_magenta/lineup.png)
- [potion_medium_obsidian](potions/potion_medium_obsidian/lineup.png)
- [potion_medium_orange](potions/potion_medium_orange/lineup.png)
- [potion_medium_purple](potions/potion_medium_purple/lineup.png)
- [potion_medium_silver](potions/potion_medium_silver/lineup.png)
- [potion_medium_teal](potions/potion_medium_teal/lineup.png)
- [potion_small_cobalt](potions/potion_small_cobalt/lineup.png)
- [potion_small_coral](potions/potion_small_coral/lineup.png)
- [potion_small_crimson](potions/potion_small_crimson/lineup.png)
- [potion_small_emerald](potions/potion_small_emerald/lineup.png)
- [potion_small_gold](potions/potion_small_gold/lineup.png)
- [potion_small_lime](potions/potion_small_lime/lineup.png)
- [potion_small_magenta](potions/potion_small_magenta/lineup.png)
- [potion_small_obsidian](potions/potion_small_obsidian/lineup.png)
- [potion_small_orange](potions/potion_small_orange/lineup.png)
- [potion_small_purple](potions/potion_small_purple/lineup.png)
- [potion_small_silver](potions/potion_small_silver/lineup.png)
- [potion_small_teal](potions/potion_small_teal/lineup.png)

### Scrolls

- [scroll_cobalt_raidho](scrolls/scroll_cobalt_raidho/lineup.png)
- [scroll_coral_sowilo](scrolls/scroll_coral_sowilo/lineup.png)
- [scroll_crimson_othala](scrolls/scroll_crimson_othala/lineup.png)
- [scroll_emerald_fehu](scrolls/scroll_emerald_fehu/lineup.png)
- [scroll_gold_algiz](scrolls/scroll_gold_algiz/lineup.png)
- [scroll_lime_hagalaz](scrolls/scroll_lime_hagalaz/lineup.png)
- [scroll_magenta_berkano](scrolls/scroll_magenta_berkano/lineup.png)
- [scroll_obsidian_tiwaz](scrolls/scroll_obsidian_tiwaz/lineup.png)
- [scroll_orange_thurisaz](scrolls/scroll_orange_thurisaz/lineup.png)
- [scroll_purple_dagaz](scrolls/scroll_purple_dagaz/lineup.png)
- [scroll_silver_ingwaz](scrolls/scroll_silver_ingwaz/lineup.png)
- [scroll_teal_eihwaz](scrolls/scroll_teal_eihwaz/lineup.png)

### Shields

- [shield_aegis_gold](shields/shield_aegis_gold/lineup.png)
- [shield_buckler_spiked](shields/shield_buckler_spiked/lineup.png)
- [shield_dragon_scale](shields/shield_dragon_scale/lineup.png)
- [shield_heater_iron](shields/shield_heater_iron/lineup.png)
- [shield_heroic_quarters](shields/shield_heroic_quarters/lineup.png)
- [shield_heroic_sun](shields/shield_heroic_sun/lineup.png)
- [shield_iron_tower](shields/shield_iron_tower/lineup.png)
- [shield_kite_crusader](shields/shield_kite_crusader/lineup.png)
- [shield_round_wood](shields/shield_round_wood/lineup.png)
- [shield_royal_crown](shields/shield_royal_crown/lineup.png)
- [shield_royal_lily](shields/shield_royal_lily/lineup.png)
- [shield_scutum_wood](shields/shield_scutum_wood/lineup.png)
- [shield_wood_simple](shields/shield_wood_simple/lineup.png)

### Tools

- [tool_bell_silver](tools/tool_bell_silver/lineup.png)
- [tool_candelabrum](tools/tool_candelabrum/lineup.png)
- [tool_candle_tallow](tools/tool_candle_tallow/lineup.png)
- [tool_grappling_hook](tools/tool_grappling_hook/lineup.png)
- [tool_horn_bugle](tools/tool_horn_bugle/lineup.png)
- [tool_lamp_oil](tools/tool_lamp_oil/lineup.png)
- [tool_lantern_brass](tools/tool_lantern_brass/lineup.png)
- [tool_lockpick_set](tools/tool_lockpick_set/lineup.png)
- [tool_mirror_hand](tools/tool_mirror_hand/lineup.png)
- [tool_pickaxe_dwarven](tools/tool_pickaxe_dwarven/lineup.png)
- [tool_torch_iron](tools/tool_torch_iron/lineup.png)
- [tool_torch_wooden](tools/tool_torch_wooden/lineup.png)

### Weapons

- [axe_bearded](weapons/axe_bearded/lineup.png)
- [axe_executioner](weapons/axe_executioner/lineup.png)
- [battleaxe_gilded](weapons/battleaxe_gilded/lineup.png)
- [battleaxe_iron](weapons/battleaxe_iron/lineup.png)
- [broadsword_gilded](weapons/broadsword_gilded/lineup.png)
- [broadsword_iron](weapons/broadsword_iron/lineup.png)
- [broadsword_steel](weapons/broadsword_steel/lineup.png)
- [club_steel](weapons/club_steel/lineup.png)
- [club_wood_old](weapons/club_wood_old/lineup.png)
- [crossbow_gilded](weapons/crossbow_gilded/lineup.png)
- [dagger_kukri](weapons/dagger_kukri/lineup.png)
- [dagger_nightblade](weapons/dagger_nightblade/lineup.png)
- [dagger_short](weapons/dagger_short/lineup.png)
- [greatsword_steel](weapons/greatsword_steel/lineup.png)
- [halberd_steel](weapons/halberd_steel/lineup.png)
- [hammer_gilded](weapons/hammer_gilded/lineup.png)
- [hammer_mallet](weapons/hammer_mallet/lineup.png)
- [morning_star_iron](weapons/morning_star_iron/lineup.png)
- [rapier_steel](weapons/rapier_steel/lineup.png)
- [saber_steel](weapons/saber_steel/lineup.png)
- [scythe_iron](weapons/scythe_iron/lineup.png)
- [spear_steel](weapons/spear_steel/lineup.png)
- [staff_archmage](weapons/staff_archmage/lineup.png)
- [staff_emerald](weapons/staff_emerald/lineup.png)
- [sword_knight](weapons/sword_knight/lineup.png)
- [sword_sunblade](weapons/sword_sunblade/lineup.png)
- [wand_celestial](weapons/wand_celestial/lineup.png)
- [wand_ruby](weapons/wand_ruby/lineup.png)
- [warhammer_iron](weapons/warhammer_iron/lineup.png)
- [warhammer_spiked](weapons/warhammer_spiked/lineup.png)
