"""Delete saved outputs that are still NaN/black (max <= 8) so a fresh process re-runs them."""
import sys, pathlib
import numpy as np
from PIL import Image
n = 0
for p in pathlib.Path(sys.argv[1]).glob("*.png"):
    if np.asarray(Image.open(p).convert("RGB")).max() <= 8:
        p.unlink(); n += 1; print("black, removed", p.name)
print("black files:", n)
