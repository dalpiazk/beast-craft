# Beast Craft ArtLab, remaining-seven finals (2026-09-26): the golem's face tools; imported by finish.py, and the eye-restore method frostfix.py follows.
"""Final golem face protection (round 4's detail pass turned the eyes into glow spots).

  python golem_face.py restore LOCK.png OUT.png   -> #10's own eyes (amber disk + dark rim) pasted onto the lock,
                                                      each eye aligned by template matching (+-14 px), feather 1.5 px
  python golem_face.py masks LOCK.png             -> work/golem_dmask_body.png (character minus face, for the main
                                                      detail pass) + work/golem_dmask_face.png (face only, 2x size)
"""
import sys
from common import *
import cv2
from colour import char_mask

PICK = WORK / "golem10_pick_src.png"
EYES = [(125, 609), (181, 607)]      # measured on #10 (896x1152)
R_EYE = 23
FACE = ((158, 622), (84, 66))         # ellipse centre, half-axes (1x) around the pale round face


def restore(lock_p, out_p):
    pick = np.asarray(Image.open(PICK).convert("RGB").resize((W, H), Image.LANCZOS))
    lk = np.asarray(Image.open(lock_p).convert("RGB"))
    out = lk.astype(np.float32).copy()
    log = []
    for x, y in EYES:
        t = pick[y - 30:y + 30, x - 30:x + 30]
        win = lk[y - 44:y + 44, x - 44:x + 44]
        r = cv2.matchTemplate(cv2.cvtColor(win, cv2.COLOR_RGB2GRAY), cv2.cvtColor(t, cv2.COLOR_RGB2GRAY), cv2.TM_CCOEFF_NORMED)
        _, sc, _, (dx, dy) = cv2.minMaxLoc(r)
        dx, dy = dx - 14, dy - 14
        m = np.zeros((H, W), np.uint8); cv2.circle(m, (x + dx, y + dy), R_EYE, 1, -1)
        a = cv2.GaussianBlur(m.astype(np.float32), (0, 0), 1.5)[..., None]
        shifted = np.roll(np.roll(pick, dy, 0), dx, 1).astype(np.float32)
        out = out * (1 - a) + shifted * a
        log.append(dict(eye=[x, y], shift=[dx, dy], match=round(float(sc), 3)))
    Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(out_p)
    print(json.dumps(log))


def masks(lock_p):
    lk = np.asarray(Image.open(lock_p).convert("RGB"))
    m = char_mask(lk).astype(np.uint8)
    face = np.zeros((H, W), np.uint8); cv2.ellipse(face, FACE[0], FACE[1], 0, 0, 360, 1, -1)
    body = m & (1 - cv2.dilate(face, np.ones((25, 25), np.uint8)))      # 12 px safety band around the face
    for name, mm in (("body", body), ("face", face)):
        Image.fromarray(mm * 255).resize((2 * W, 2 * H), Image.NEAREST).save(WORK / f"golem_dmask_{name}.png")
    print("masks ok", body.mean(), face.mean())


if __name__ == "__main__":
    {"restore": lambda: restore(sys.argv[2], sys.argv[3]), "masks": lambda: masks(sys.argv[2])}[sys.argv[1]]()
