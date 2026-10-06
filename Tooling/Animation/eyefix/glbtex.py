"""GLB image extract / splice. Splice rewrites ONLY the image bufferView bytes (+ mimeType); every other
bufferView is copied verbatim (re-aligned to 4 bytes), so accessors/geometry/skin/animation data are
byte-identical. compare() proves it."""
import json, struct, sys, io, hashlib


def read(path):
    b = open(path, 'rb').read()
    assert b[:4] == b'glTF'
    off, js, bin_ = 12, None, None
    while off < len(b):
        ln, typ = struct.unpack_from('<I4s', b, off); off += 8
        ch = b[off:off+ln]; off += ln
        if typ == b'JSON': js = json.loads(ch)
        elif typ[:3] == b'BIN': bin_ = ch
    return js, bin_


def bv_bytes(js, bin_, i):
    bv = js['bufferViews'][i]; o = bv.get('byteOffset', 0)
    return bin_[o:o+bv['byteLength']]


def extract(path, img_index=0):
    js, bin_ = read(path)
    im = js['images'][img_index]
    return bv_bytes(js, bin_, im['bufferView']), im['mimeType']


def splice(src, dst, data, mime, img_index=0):
    js, bin_ = read(src)
    ib = js['images'][img_index]['bufferView']
    order = sorted(range(len(js['bufferViews'])), key=lambda i: js['bufferViews'][i].get('byteOffset', 0))
    new = bytearray()
    for i in order:
        chunk = data if i == ib else bv_bytes(js, bin_, i)
        while len(new) % 4: new.append(0)
        js['bufferViews'][i]['byteOffset'] = len(new)
        js['bufferViews'][i]['byteLength'] = len(chunk)
        new += chunk
    while len(new) % 4: new.append(0)
    js['buffers'][0]['byteLength'] = len(new)
    js['images'][img_index]['mimeType'] = mime
    jb = json.dumps(js, separators=(',', ':')).encode()
    while len(jb) % 4: jb += b' '
    total = 12 + 8 + len(jb) + 8 + len(new)
    out = struct.pack('<4sII', b'glTF', 2, total) + struct.pack('<I4s', len(jb), b'JSON') + jb + struct.pack('<I4s', len(new), b'BIN\x00') + bytes(new)
    open(dst, 'wb').write(out)


def compare(a, b, img_index=0):
    ja, ba = read(a); jb, bb = read(b)
    ia = ja['images'][img_index]['bufferView']
    strip = lambda js: {k: v for k, v in js.items() if k not in ('bufferViews', 'buffers', 'images')}
    rep = {'json_equal_except_bufferViews_buffers_images': strip(ja) == strip(jb)}
    rep['images_equal_except_mime'] = [{k: v for k, v in x.items() if k != 'mimeType'} for x in ja['images']] == \
                                      [{k: v for k, v in x.items() if k != 'mimeType'} for x in jb['images']]
    norm = lambda bv: {k: v for k, v in bv.items() if k not in ('byteOffset', 'byteLength')}
    rep['bufferViews_equal_except_offsets'] = [norm(x) for x in ja['bufferViews']] == [norm(x) for x in jb['bufferViews']]
    diff = [i for i in range(len(ja['bufferViews'])) if bv_bytes(ja, ba, i) != bv_bytes(jb, bb, i)]
    rep['bufferViews_with_different_bytes'] = diff
    rep['image_bufferView'] = ia
    acc_diff = []
    for i, acc in enumerate(ja.get('accessors', [])):
        bv = acc.get('bufferView')
        if bv is None: continue
        if bv_bytes(ja, ba, bv) != bv_bytes(jb, bb, bv): acc_diff.append(i)
    rep['accessors_total'] = len(ja.get('accessors', []))
    rep['accessors_with_different_bytes'] = acc_diff
    rep['sha_nonimage_a'] = hashlib.sha256(b''.join(bv_bytes(ja, ba, i) for i in range(len(ja['bufferViews'])) if i != ia)).hexdigest()
    rep['sha_nonimage_b'] = hashlib.sha256(b''.join(bv_bytes(jb, bb, i) for i in range(len(jb['bufferViews'])) if i != ia)).hexdigest()
    rep['ok'] = rep['json_equal_except_bufferViews_buffers_images'] and rep['images_equal_except_mime'] and \
        rep['bufferViews_equal_except_offsets'] and diff == [ia] and not acc_diff and rep['sha_nonimage_a'] == rep['sha_nonimage_b']
    return rep


if __name__ == '__main__':
    cmd = sys.argv[1]
    if cmd == 'extract':
        data, mime = extract(sys.argv[2]); open(sys.argv[3], 'wb').write(data); print(mime, len(data))
    elif cmd == 'splice':
        data = open(sys.argv[4], 'rb').read()
        mime = 'image/png' if data[:4] == b'\x89PNG' else 'image/jpeg'
        splice(sys.argv[2], sys.argv[3], data, mime); print('spliced', mime, len(data))
    elif cmd == 'compare':
        print(json.dumps(compare(sys.argv[2], sys.argv[3]), indent=1))
