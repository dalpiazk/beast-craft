"""Inspect a .glb file's tri count, texture sizes, node/skin info, without any 3D library.

Pure standard library: parses the GLB binary container (12-byte header + JSON chunk + BIN chunk)
and reads embedded image dimensions straight from PNG/JPEG headers inside the BIN chunk.

Usage: python gltf_inspect.py MODEL.glb
"""
import json
import struct
import sys


def read_glb(path):
    with open(path, "rb") as f:
        data = f.read()
    magic, version, length = struct.unpack_from("<4sII", data, 0)
    assert magic == b"glTF", f"not a GLB file: {magic!r}"
    off = 12
    json_chunk = None
    bin_chunk = None
    while off < length:
        clen, ctype = struct.unpack_from("<I4s", data, off)
        off += 8
        chunk = data[off:off + clen]
        off += clen
        if ctype == b"JSON":
            json_chunk = json.loads(chunk.decode("utf-8"))
        elif ctype == b"BIN\x00":
            bin_chunk = chunk
    return json_chunk, bin_chunk, len(data)


def png_size(buf):
    if buf[:8] == b"\x89PNG\r\n\x1a\n":
        w, h = struct.unpack(">II", buf[16:24])
        return w, h
    return None


def jpeg_size(buf):
    if buf[:2] != b"\xff\xd8":
        return None
    i = 2
    while i < len(buf):
        if buf[i] != 0xFF:
            i += 1
            continue
        marker = buf[i + 1]
        if marker in (0xC0, 0xC1, 0xC2, 0xC3):
            h, w = struct.unpack(">HH", buf[i + 5:i + 9])
            return w, h
        seglen = struct.unpack(">H", buf[i + 2:i + 4])[0]
        i += 2 + seglen
    return None


def main():
    path = sys.argv[1]
    gltf, binchunk, filesize = read_glb(path)

    print(f"File: {path}")
    print(f"File size: {filesize:,} bytes ({filesize/1024/1024:.2f} MB)")
    print(f"asset.generator: {gltf.get('asset', {}).get('generator', '?')}")
    print(f"asset.version:   {gltf.get('asset', {}).get('version', '?')}")

    meshes = gltf.get("meshes", [])
    accessors = gltf.get("accessors", [])
    total_tris = 0
    total_verts = 0
    for m in meshes:
        for prim in m.get("primitives", []):
            mode = prim.get("mode", 4)  # 4 = TRIANGLES
            if "indices" in prim:
                idx_acc = accessors[prim["indices"]]
                count = idx_acc["count"]
                tris = count // 3 if mode == 4 else 0
            else:
                pos_acc = accessors[prim["attributes"]["POSITION"]]
                tris = pos_acc["count"] // 3
            pos_acc = accessors[prim["attributes"]["POSITION"]]
            total_verts += pos_acc["count"]
            total_tris += tris
    print(f"Meshes: {len(meshes)}, primitives total tris: {total_tris:,}, verts (sum, may double count shared): {total_verts:,}")

    nodes = gltf.get("nodes", [])
    print(f"Nodes: {len(nodes)}")
    skins = gltf.get("skins", [])
    print(f"Skins (existing rig): {len(skins)}")
    anims = gltf.get("animations", [])
    print(f"Animations: {len(anims)}")

    materials = gltf.get("materials", [])
    print(f"Materials: {len(materials)}")

    images = gltf.get("images", [])
    buffer_views = gltf.get("bufferViews", [])
    print(f"Images: {len(images)}")
    for i, im in enumerate(images):
        name = im.get("name", f"image{i}")
        if "bufferView" in im and binchunk is not None:
            bv = buffer_views[im["bufferView"]]
            start = bv.get("byteOffset", 0)
            length = bv["byteLength"]
            buf = binchunk[start:start + length]
            size = png_size(buf) or jpeg_size(buf)
            mime = im.get("mimeType", "?")
            print(f"  [{i}] {name}: {mime}, {length:,} bytes, dimensions={size}")
        else:
            print(f"  [{i}] {name}: (external or no bufferView) {im}")

    # bounding box from POSITION accessor min/max on the first mesh primitive, if present
    for m in meshes:
        for prim in m.get("primitives", []):
            pos_acc = accessors[prim["attributes"]["POSITION"]]
            if "min" in pos_acc and "max" in pos_acc:
                print(f"Mesh bounds (local, pre-node-transform): min={pos_acc['min']} max={pos_acc['max']}")

    # node transforms (scale/translation hints)
    for i, n in enumerate(nodes):
        if "mesh" in n or "scale" in n or "translation" in n:
            print(f"Node[{i}] name={n.get('name')!r} mesh={n.get('mesh')} "
                  f"T={n.get('translation')} R={n.get('rotation')} S={n.get('scale')}")


if __name__ == "__main__":
    main()
