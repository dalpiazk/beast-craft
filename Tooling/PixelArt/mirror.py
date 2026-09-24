"""Authoring helper: mirror a left-half grid (<=16 cols) into a symmetric 32-col grid.
Usage: python mirror.py half.txt > full rows (then hand-edit asymmetric details)."""
import sys
for line in open(sys.argv[1]).read().splitlines():
    if line.startswith('#'): print(line); continue
    l = line.ljust(16, '.')[:16]
    print(l + l[::-1])
