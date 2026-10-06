"""compose.py ORIG_TEX OUTDIR OUT_PNG : apply projection ops (from eyefix_bl.py) to the original texture."""
import sys, os, json, numpy as np
from PIL import Image

orig, outdir, outpng = sys.argv[1:4]
meta = json.load(open(os.path.join(outdir, 'proj_meta.json')))
Z = np.load(os.path.join(outdir, 'proj.npz'))
base = np.asarray(Image.open(orig).convert('RGB')).astype(np.float64)
H, W = base.shape[:2]
img = base.copy(); wtot = np.zeros((H, W))


def bilinear(I, x, y):
    h, w = I.shape[:2]
    x = np.clip(x, 0, w-1.001); y = np.clip(y, 0, h-1.001)
    x0 = np.floor(x).astype(int); y0 = np.floor(y).astype(int); fx = (x-x0)[:, None]; fy = (y-y0)[:, None]
    return (I[y0, x0]*(1-fx)*(1-fy) + I[y0, x0+1]*fx*(1-fy) + I[y0+1, x0]*(1-fx)*fy + I[y0+1, x0+1]*fx*fy)


for k in range(meta['nops']):
    cam = json.load(open(os.path.join(outdir, f'op{k}_cam.json')))
    src = np.asarray(Image.open(cam['src']).convert('RGB')).astype(np.float64)
    ys, xs, w, px, py = (Z[f'{k}_{n}'] for n in ('ys', 'xs', 'w', 'px', 'py'))
    col = np.mean([bilinear(src, px[:, j], py[:, j]) for j in range(px.shape[1])], axis=0) if px.ndim == 2 else bilinear(src, px, py)
    pk = json.load(open(os.path.join(outdir, 'cfg.json')))['ops'][k].get('protect_gr')
    if pk:   # keep saturated red/orange target texels (beak, crest): soft key on original G/R
        o = base[ys, xs]; gr = o[:, 1]/np.maximum(o[:, 0], 1)
        w = w*np.clip((gr - pk[0])/(pk[1] - pk[0]), 0, 1)
    img[ys, xs] = img[ys, xs]*(1-w[:, None]) + col*w[:, None]
    wtot[ys, xs] = np.maximum(wtot[ys, xs], w)

out8 = np.clip(np.round(img), 0, 255).astype(np.uint8)
orig8 = np.asarray(Image.open(orig).convert('RGB'))
Image.fromarray(out8).save(outpng, optimize=True)
changed = np.any(out8 != orig8, axis=2)
outside = changed & (wtot <= 0)
print(json.dumps({'texels_masked': int((wtot > 0).sum()), 'texels_changed': int(changed.sum()),
                  'changed_outside_mask': int(outside.sum()),
                  'reload_identical': bool(np.array_equal(np.asarray(Image.open(outpng).convert('RGB')), out8))}))
Image.fromarray((np.clip(wtot, 0, 1)*255).astype(np.uint8)).save(os.path.join(outdir, 'mask.png'))
