# Beast Craft ArtLab, remaining-seven finals (2026-09-26): the face-protection masks of the seven finals (per-beast ellipse in each provenance file).
"""New-beast finals, face protection (golem_face.py masks, generalised): body mask = character minus the face
ellipse (+12 px band), face mask = the ellipse; both at 2x for detail_pass.py.
  python facemask.py BEAST LOCK.png CX CY AX AY      (1x coords)"""
import sys
from common import *
import cv2
from colour import char_mask
b, lock_p = sys.argv[1], sys.argv[2]; cx, cy, ax, ay = [int(v) for v in sys.argv[3:7]]
lk = np.asarray(Image.open(lock_p).convert("RGB"))
m = char_mask(lk).astype(np.uint8)
face = np.zeros((H, W), np.uint8); cv2.ellipse(face, (cx, cy), (ax, ay), 0, 0, 360, 1, -1)
body = m & (1 - cv2.dilate(face, np.ones((25, 25), np.uint8)))
for name, mm in (("body", body), ("face", face)):
    Image.fromarray(mm * 255).resize((2 * W, 2 * H), Image.NEAREST).save(WORK / f"{b}_dmask_{name}.png")
json.dump(dict(face_ellipse_1x=[cx, cy, ax, ay]), open(WORK / f"{b}_face.json", "w"))
print("masks ok", body.mean(), face.mean())
