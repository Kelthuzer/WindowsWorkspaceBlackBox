"""Reject truncated .NET single-file EXEs before packaging or distributing them."""
import json
import struct
import sys
from pathlib import Path

data = Path(sys.argv[1]).read_bytes()
signature = bytes.fromhex('8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae')
signature_at = data.find(signature)
assert data[:2] == b'MZ' and signature_at >= 8, 'Missing PE/bundle signature'
header = struct.unpack_from('<q', data, signature_at - 8)[0]
assert 0 < header < len(data) - 12, 'Bundle header is outside file: truncated EXE'
major, minor, count = struct.unpack_from('<IIi', data, header)
assert major == 6 and 1 <= count <= 10000, f'Unsupported bundle version/count: {major}.{minor}/{count}'
position = header + 12

def string():
    global position
    length = 0
    shift = 0
    while True:
        byte = data[position]
        position += 1
        length |= (byte & 127) << shift
        if not byte & 128:
            break
        shift += 7
        assert shift <= 28
    value = data[position:position + length].decode('utf-8')
    position += length
    return value

bundle_id = string()
position += 40  # deps/runtimeconfig locations and flags
entries = {}
for _ in range(count):
    offset, size, compressed, kind = struct.unpack_from('<qqqB', data, position)
    position += 25
    name = string()
    assert 0 <= offset <= header and 0 <= size and 0 <= compressed
    assert offset + (compressed or size) <= header, f'Truncated bundle entry: {name}'
    entries[name] = (offset, size, compressed)
for required in ['WindowsWorkspaceBlackBox.dll', 'WWBB.Core.dll', 'System.Private.CoreLib.dll', 'System.Windows.Forms.dll']:
    assert required in entries, f'Missing self-contained dependency: {required}'
assert position <= len(data)
print(json.dumps({'file': sys.argv[1], 'bytes': len(data), 'entries': count, 'bundle_id': bundle_id, 'valid': True}))
