"""Validate the scoped ARM64 edit on original and currently prepared clients."""
import hashlib
import json
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(root/'android-client'))
import discovery_original as discovery
import presentation_original as presentation

source = root/'Durango original'
current = root/'android-client/work/original-nexon/client'
original = (source/discovery.NATIVE).read_bytes()
baseline = (current/discovery.NATIVE).read_bytes()
baseline_report = presentation.verify(source, current)
modified = discovery.patch(baseline)
report = discovery.verify_delta(baseline, modified)
assert discovery.patch(original)[0x18769e8:0x18769ec] == discovery.AFTER
for site in discovery.SITES:
    broken = bytearray(baseline)
    broken[site] ^= 1
    try:
        discovery.patch(broken)
    except AssertionError:
        pass
    else:
        raise AssertionError('Patch accepted unexpected ARM64 input')
destination = root/'android-client/work/discovery-cache'
destination.mkdir(parents=True, exist_ok=True)
(destination/'libil2cpp.so').write_bytes(modified)
report['input_sha256'] = hashlib.sha256(baseline).hexdigest()
report['output_sha256'] = hashlib.sha256(modified).hexdigest()
report['original_sha256'] = hashlib.sha256(original).hexdigest()
report['anchor_mismatch_rejected'] = True
report['existing_presentation_and_estate_changes_verified'] = True
(destination/'verification.json').write_text(json.dumps(report, indent=2)+'\n', 'utf8')
print(json.dumps(report))
