"""jpegenc.py ORIG.jpg EDITED.png MASK.png OUT.jpg : re-encode with the original's quant tables + subsampling; report diff stats."""
import sys, io, json, numpy as np
from PIL import Image, JpegImagePlugin
o = Image.open(sys.argv[1]); e = Image.open(sys.argv[2]).convert('RGB'); M = np.asarray(Image.open(sys.argv[3])) > 0
samp = JpegImagePlugin.get_sampling(o)
res = {}
for name, kw in [('orig_qtables', dict(qtables=o.quantization, subsampling=samp)), ('q95_444', dict(quality=95, subsampling=0))]:
    buf = io.BytesIO(); e.save(buf, 'JPEG', optimize=True, **kw); data = buf.getvalue()
    D = np.asarray(Image.open(io.BytesIO(data)).convert('RGB')).astype(int); O = np.asarray(o.convert('RGB')).astype(int); E = np.asarray(e).astype(int)
    out = ~M
    res[name] = {'bytes': len(data), 'outside_mask_vs_orig_mean_abs': float(np.abs(D-O)[out].mean()), 'outside_p99': float(np.percentile(np.abs(D-O)[out], 99)), 'outside_max': int(np.abs(D-O)[out].max()),
                 'inside_mask_vs_edit_mean_abs': float(np.abs(D-E)[M].mean())}
    if name == 'q95_444': open(sys.argv[4], 'wb').write(data)
# reference: JPEG noise of re-encoding the untouched original once
buf = io.BytesIO(); o.convert('RGB').save(buf, 'JPEG', qtables=o.quantization, subsampling=samp); R = np.asarray(Image.open(buf).convert('RGB')).astype(int)
res['reference_reencode_orig_noise'] = {'mean_abs': float(np.abs(R-np.asarray(o.convert('RGB')).astype(int)).mean()), 'max': int(np.abs(R-np.asarray(o.convert('RGB')).astype(int)).max())}
res['orig_bytes'] = len(open(sys.argv[1], 'rb').read()); res['subsampling'] = samp
print(json.dumps(res, indent=1))
