"""Face protection for a beast whose small face features the pipeline loses (the final golem, pick #10).

The soft init (blur sigma 10) erases small eyes, the lock repaints them as pale rings, and the detail pass turns them
into glow spots. Three steps keep the pick's own face:

  python face.py init BEAST PICK.png       paste the pick's eyes into the soft init before the lock
                                           ($ARTLAB_WORK/sketch_{beast}_soft.png; the eyeless one is kept as
                                           sketch_{beast}_soft_noeyes.png)
  python face.py restore BEAST PICK.png LOCK.png OUT.png
                                           paste the pick's eyes onto the chosen lock seed, each aligned by template
                                           matching (+-14 px), feathered 1.5 px
  python face.py masks BEAST LOCK.png      detail-pass masks: $ARTLAB_WORK/{beast}_dmask_body.png (character minus
                                           the face and a 12 px band) and {beast}_dmask_face.png (face only), at 2x

Coordinates are measured on the pick at 896x1152 (FACES below). Only the golem needed this.
"""
import sys

from common import *
import cv2

FACES = {
    "golem": dict(
        eyes=[(125, 609), (181, 607)],   # eye centres on #10
        init_r=26,                       # radius pasted into the soft init (amber disk + dark rim)
        restore_r=23,                    # radius pasted onto the lock
        face=((158, 622), (84, 66)),     # ellipse centre, half-axes around the pale round face
    ),
}
FACE = FACES["golem"]["face"]            # finish.py --peel-paper protects this ellipse


def _pick(path):
    return np.asarray(Image.open(path).convert("RGB").resize((W, H), Image.LANCZOS))


def init(beast, pick_p):
    f = FACES[beast]
    pick = _pick(pick_p).astype(np.float32)
    soft_p, keep_p = WORK / f"sketch_{beast}_soft.png", WORK / f"sketch_{beast}_soft_noeyes.png"
    if not keep_p.exists():
        Image.open(soft_p).save(keep_p)
    m = np.zeros((H, W), np.uint8)
    for x, y in f["eyes"]:
        cv2.circle(m, (x, y), f["init_r"], 1, -1)
    a = cv2.GaussianBlur(m.astype(np.float32), (0, 0), 1.5)[..., None]
    soft = np.asarray(Image.open(keep_p).convert("RGB")).astype(np.float32)
    Image.fromarray((soft * (1 - a) + pick * a).clip(0, 255).astype(np.uint8)).save(soft_p)
    Image.fromarray(m * 255).save(WORK / f"{beast}_eyes_mask.png")
    print("eyes pasted into", soft_p)


def restore(beast, pick_p, lock_p, out_p):
    f = FACES[beast]
    pick = _pick(pick_p)
    lk = np.asarray(Image.open(lock_p).convert("RGB"))
    out = lk.astype(np.float32).copy()
    log = []
    for x, y in f["eyes"]:
        t = pick[y - 30:y + 30, x - 30:x + 30]
        win = lk[y - 44:y + 44, x - 44:x + 44]
        r = cv2.matchTemplate(cv2.cvtColor(win, cv2.COLOR_RGB2GRAY), cv2.cvtColor(t, cv2.COLOR_RGB2GRAY), cv2.TM_CCOEFF_NORMED)
        _, sc, _, (dx, dy) = cv2.minMaxLoc(r)
        dx, dy = dx - 14, dy - 14
        m = np.zeros((H, W), np.uint8)
        cv2.circle(m, (x + dx, y + dy), f["restore_r"], 1, -1)
        a = cv2.GaussianBlur(m.astype(np.float32), (0, 0), 1.5)[..., None]
        shifted = np.roll(np.roll(pick, dy, 0), dx, 1).astype(np.float32)
        out = out * (1 - a) + shifted * a
        log.append(dict(eye=[x, y], shift=[dx, dy], match=round(float(sc), 3)))
    Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(out_p)
    print(json.dumps(log))


def masks(beast, lock_p):
    from colour import char_mask
    f = FACES[beast]
    lk = np.asarray(Image.open(lock_p).convert("RGB"))
    m = char_mask(lk).astype(np.uint8)
    face = np.zeros((H, W), np.uint8)
    cv2.ellipse(face, f["face"][0], f["face"][1], 0, 0, 360, 1, -1)
    body = m & (1 - cv2.dilate(face, np.ones((25, 25), np.uint8)))      # 12 px safety band around the face
    for name, mm in (("body", body), ("face", face)):
        Image.fromarray(mm * 255).resize((2 * W, 2 * H), Image.NEAREST).save(WORK / f"{beast}_dmask_{name}.png")
    print("masks ok", body.mean(), face.mean())


if __name__ == "__main__":
    cmd, args = sys.argv[1], sys.argv[2:]
    {"init": init, "restore": restore, "masks": masks}[cmd](*args)
