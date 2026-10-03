"""Token-count checker for the menu-backdrop prompts in gen_backdrops.py. Mirrors the approach of
`../tokcheck.py` (the repo's beast/enemy prompt checker) for this directory's own REGIONS dict: every
prompt is checked against both SDXL tokenizers (`tokenizer`, `tokenizer_2`), since CLIP silently
truncates anything past 77 tokens without erroring -- the failure mode that drove most of the fix
rounds recorded in `../../provenance/menu-backdrops.md`.

Usage: python tokcheck.py
Exits 0 if every prompt fits both tokenizers' 77-token window, 1 otherwise.
"""
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))
from common import BASE  # noqa: E402
from transformers import CLIPTokenizer  # noqa: E402

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import gen_backdrops as gb  # noqa: E402

tok1 = CLIPTokenizer.from_pretrained(BASE, subfolder="tokenizer")
tok2 = CLIPTokenizer.from_pretrained(BASE, subfolder="tokenizer_2")

bad = 0
rows = []
for rid, region in gb.REGIONS.items():
    rname, prompt, negative = region["name"], region["prompt"], region["negative"]
    n1 = len(tok1(prompt).input_ids)
    n2 = len(tok2(prompt).input_ids)
    nn1 = len(tok1(negative).input_ids)
    nn2 = len(tok2(negative).input_ids)
    over = n1 > 77 or n2 > 77 or nn1 > 77 or nn2 > 77
    bad += over
    rows.append((rid, rname, n1, n2, nn1, nn2, over))
    print(f"{rid} {rname:16s} pos(tok1={n1:3d} tok2={n2:3d}) neg(tok1={nn1:3d} tok2={nn2:3d}) "
          f"{'OVER' if over else 'ok'}")

print(f"\n{bad} of {len(rows)} prompts over budget" if bad else "\nAll prompts fit the 77-token window.")
sys.exit(1 if bad else 0)
