"""Refresh discovery queries and show career text hints, retaining other client bytes."""
from pathlib import Path
import struct

NATIVE = Path('lib/arm64-v8a/libil2cpp.so')
# Original Android 5.2.1 ARM64. IL2CPP metadata identifies MapSystem.OnReady
# and GetDiscoveryInfos. W3 is the refresh argument of AsyncCachedDictionary.Request.
SITES = {0x18769e8: bytes.fromhex('e3031f2ae00314aa'),
         0x1879498: bytes.fromhex('e3031f2a040140f9'),
         # LearningGuideGroup.ShowHintPopup(Advice): CardNewsPopup.Load,
         # followed by TBZ W0. False routes to SimpleTextListPopup.GetHints.
         0x17baeb8: bytes.fromhex('0d4efd9760000036')}
BEFORE = struct.pack('<I', 0x2a1f03e3)  # MOV W3, WZR (false)
AFTER = struct.pack('<I', 0x52800023)   # MOV W3, #1 (true)
PATCHES = {0x18769e8: AFTER, 0x1879498: AFTER,
           0x17baeb8: struct.pack('<I', 0x2a1f03e0)}  # MOV W0, WZR: use text hints

def patch(data):
    result = bytearray(data)
    for site, anchor in SITES.items():
        assert data[site:site+len(anchor)] == anchor, f'Unexpected discovery query at {site:#x}'
        result[site:site+4] = PATCHES[site]
    return bytes(result)

def apply(client):
    path = client / NATIVE
    path.write_bytes(patch(path.read_bytes()))

def verify_delta(before, after):
    assert len(before) == len(after)
    assert after == patch(before), 'Unrelated client bytes changed'
    assert all(after[site:site+4] == PATCHES[site] for site in SITES)
    return {'queries': ['MapSystem.OnReady', 'MapSystem.GetDiscoveryInfos'],
            'refresh': True, 'career_text_hints': True, 'instruction_bytes': 12, 'other_client_bytes_preserved': True}

def verify(source, client):
    original = (source / NATIVE).read_bytes()
    modified = (client / NATIVE).read_bytes()
    for site in SITES:
        assert original[site:site+4] == SITES[site][:4] and modified[site:site+4] == PATCHES[site]
    return {'queries': ['MapSystem.OnReady', 'MapSystem.GetDiscoveryInfos'], 'refresh': True,
            'career_text_hints': True, 'instruction_bytes': 12}
