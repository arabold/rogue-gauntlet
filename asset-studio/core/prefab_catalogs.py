"""Temporary prefab sheets with explicit, receipt-backed publication."""
import hashlib
import json
from pathlib import Path
import shutil

from core import publication
from core.paths import work_root


def inputs(studio, key):
    """Bind a sheet to its source geometry, palette and renderer."""
    values = publication.shared_hashes(studio)
    values['catalog'] = key
    return hashlib.sha256(json.dumps(values, sort_keys=True).encode()).hexdigest()


def candidate(studio, key):
    return work_root(studio) / 'prefabs' / key


def render(studio, key, items, yaws):
    from core import catalog
    folder = candidate(studio, key)
    signature = inputs(studio, key)
    publication.stage_stamp(folder, signature)
    catalog.sheet(items, yaws=yaws).save(folder / 'sheet.png')
    publication.render_stamp(folder, signature, ('sheet.png',))
    return folder / 'sheet.png'


def publish(studio, key):
    """Copy the unchanged reviewed candidate; never render during publication."""
    folder = candidate(studio, key)
    signature = inputs(studio, key)
    review = json.loads((folder / 'review.json').read_text())
    if review['inputs'] != signature:
        raise ValueError('Prefab inputs changed; render and review again')
    if review['files']['sheet.png'] != publication.digest(folder / 'sheet.png'):
        raise ValueError('Prefab sheet changed; render and review again')
    destination = Path(studio) / 'catalogs' / 'prefabs'
    destination.mkdir(parents=True, exist_ok=True)
    output = destination / f'{key}.png'
    shutil.copy2(folder / 'sheet.png', output)
    (destination / f'{key}.json').write_text(json.dumps(
        dict(inputs=signature, file=output.name, sha256=publication.digest(output)), indent=2) + '\n')
    return output
