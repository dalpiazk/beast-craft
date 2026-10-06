"""glbclean.py SRC DST [IMAGE] : drop normal/metallicRoughness texture refs that alias the base-colour image (Live3D/Toon.fx
reads base colour only), drop the then-unused texture entries, optionally swap the base-colour image bytes. Every
bufferView other than the image is copied verbatim; accessors/nodes/skins/animations/meshes untouched."""
import sys, json, struct, glbtex

def clean(js):
    log = []
    for mi, m in enumerate(js.get('materials', [])):
        pbr = m.get('pbrMetallicRoughness', {})
        if 'baseColorTexture' not in pbr: continue
        bsrc = js['textures'][pbr['baseColorTexture']['index']]['source']
        if 'normalTexture' in m and js['textures'][m['normalTexture']['index']]['source'] == bsrc:
            del m['normalTexture']; log.append(f'material {mi}: removed normalTexture (aliased base-colour image)')
        if 'metallicRoughnessTexture' in pbr and js['textures'][pbr['metallicRoughnessTexture']['index']]['source'] == bsrc:
            del pbr['metallicRoughnessTexture']
            pbr.setdefault('metallicFactor', 0); pbr.setdefault('roughnessFactor', 0.8)
            log.append(f'material {mi}: removed metallicRoughnessTexture (aliased base-colour image); set metallicFactor=0, roughnessFactor=0.8 (glTF default would be fully metallic)')
    used = sorted({ref['index'] for m in js.get('materials', []) for ref in
                   [m.get('normalTexture'), m.get('occlusionTexture'), m.get('emissiveTexture'),
                    m.get('pbrMetallicRoughness', {}).get('baseColorTexture'), m.get('pbrMetallicRoughness', {}).get('metallicRoughnessTexture')] if ref})
    if len(used) != len(js.get('textures', [])):
        remap = {o: n for n, o in enumerate(used)}
        log.append(f'textures {len(js["textures"])} -> {len(used)}')
        js['textures'] = [js['textures'][o] for o in used]
        for m in js['materials']:
            for ref in [m.get('normalTexture'), m.get('occlusionTexture'), m.get('emissiveTexture'),
                        m.get('pbrMetallicRoughness', {}).get('baseColorTexture'), m.get('pbrMetallicRoughness', {}).get('metallicRoughnessTexture')]:
                if ref: ref['index'] = remap[ref['index']]
    return log

def rewrite(src, dst, image=None):
    js, b = glbtex.read(src)
    log = clean(js)
    ib = js['images'][0]['bufferView']
    order = sorted(range(len(js['bufferViews'])), key=lambda i: js['bufferViews'][i].get('byteOffset', 0))
    chunks = {i: glbtex.bv_bytes(js, b, i) for i in order}
    if image is not None:
        chunks[ib] = image; js['images'][0]['mimeType'] = 'image/png' if image[:4] == b'\x89PNG' else 'image/jpeg'
        log.append(f'image swapped: {len(image)} bytes {js["images"][0]["mimeType"]}')
    new = bytearray()
    for i in order:
        while len(new) % 4: new.append(0)
        js['bufferViews'][i]['byteOffset'] = len(new); js['bufferViews'][i]['byteLength'] = len(chunks[i]); new += chunks[i]
    while len(new) % 4: new.append(0)
    js['buffers'][0]['byteLength'] = len(new)
    jb = json.dumps(js, separators=(',', ':')).encode()
    while len(jb) % 4: jb += b' '
    open(dst, 'wb').write(struct.pack('<4sII', b'glTF', 2, 28 + len(jb) + len(new)) + struct.pack('<I4s', len(jb), b'JSON') + jb + struct.pack('<I4s', len(new), b'BIN\x00') + bytes(new))
    return log

def compare(a, b):
    ja, ba = glbtex.read(a); jb, bb = glbtex.read(b)
    ia = ja['images'][0]['bufferView']
    skip = ('bufferViews', 'buffers', 'images', 'textures', 'materials')
    r = {'json_equal_except_' + '_'.join(skip): {k: v for k, v in ja.items() if k not in skip} == {k: v for k, v in jb.items() if k not in skip}}
    nb = lambda x: {k: v for k, v in x.items() if k not in ('byteOffset', 'byteLength')}
    r['bufferViews_equal_except_offsets'] = [nb(x) for x in ja['bufferViews']] == [nb(x) for x in jb['bufferViews']]
    r['bufferViews_with_different_bytes'] = [i for i in range(len(ja['bufferViews'])) if glbtex.bv_bytes(ja, ba, i) != glbtex.bv_bytes(jb, bb, i)]
    r['image_bufferView'] = ia
    r['accessors_total'] = len(ja['accessors'])
    r['accessors_with_different_bytes'] = [i for i, acc in enumerate(ja['accessors']) if glbtex.bv_bytes(ja, ba, acc['bufferView']) != glbtex.bv_bytes(jb, bb, acc['bufferView'])]
    r['materials_before'] = ja['materials']; r['materials_after'] = jb['materials']
    r['textures_before'] = ja['textures']; r['textures_after'] = jb['textures']
    r['images_after'] = jb['images']
    r['ok'] = r[list(r)[0]] and r['bufferViews_equal_except_offsets'] and set(r['bufferViews_with_different_bytes']) <= {ia} and not r['accessors_with_different_bytes']
    return r

if __name__ == '__main__':
    if sys.argv[1] == 'compare': print(json.dumps(compare(sys.argv[2], sys.argv[3]), indent=1))
    else:
        img = open(sys.argv[3], 'rb').read() if len(sys.argv) > 3 else None
        print('\n'.join(rewrite(sys.argv[1], sys.argv[2], img)))
