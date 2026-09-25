"""Assert every house prompt (and any extra NAME=PROMPT argument) fits CLIP's 77-token window.

  python tokcheck.py ["name=some prompt" ...]
The SDXL pipeline silently truncates a longer prompt: the first round-2 draft lost all its style and quality tags."""
import sys
from common import BASE
from beasts import *
from transformers import CLIPTokenizer
tok = CLIPTokenizer.from_pretrained(BASE, subfolder="tokenizer")
extra = dict(a.split("=", 1) for a in sys.argv[1:])
bad = 0
items = [(f"{b} house", house_prompt(b)) for b in BEASTS] + [(f"{b} neg", house_neg(b)) for b in BEASTS] + list(extra.items())
for name, p in items:
    n = len(tok(p).input_ids); bad += n > 77
    print(f"{name:20s} {n:3d} {'OVER' if n > 77 else 'ok'}")
sys.exit(1 if bad else 0)
