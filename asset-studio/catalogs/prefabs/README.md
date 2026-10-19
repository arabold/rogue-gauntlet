# Prefab catalogs

These are reusable building blocks, rather than the finished item catalog.

- [Anatomy](anatomy.png)
- [Outfits](outfits.png)
- [Item parts](items.png)
- [Details](details.png)

Render an external review candidate with `studio.py catalog <domain>`. After inspection, use `studio.py catalog <domain> --publish` to replace the final sheet. Publication validates source and image hashes without rerendering.

Complete outfits are listed in the [costume catalog](../costumes/README.md); the outfits sheet contains reusable parts only. Harvested source tables under `prefabs/` contain metadata, without embedded previews.

Construction details include shaped timber planks, forged repair plates and corner brackets. Discover parameters with `studio.py schema details construction`; `schema details wear` includes the closed `solid_gash` cutter for prop damage.

Heraldry details include crown and lily appliqués and a ridged scale plate. Inspect `studio.py schema details heraldry` for parameters.
