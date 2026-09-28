import pathlib, json
b=pathlib.Path('briefs.py'); t=b.read_text()
t=t.replace('"sunder":           ("Metal", "plate", {"slash": True}, "an iron armour plate cracked by a slash, sparks")',
            '"sunder":           ("MetalD", "plate", {"slash": True}, "a steel armour plate split by a bronze slash, deep crack, sparks, steel grey and bronze")')
t=t.replace('"shrapnel_burst":   ("Metal", "shards", {}, "sharp metal scale shards bursting outward")',
            '"shrapnel_burst":   ("MetalD", "shards", {}, "sharp steel scale shards flying outward from a burst, steel grey and bronze")')
t=t.replace('DARKV = dict(', 'METALD = dict(PAL["Metal"], bg_in=(120, 125, 135), bg_out=(35, 38, 46), main=(170, 176, 188), dark=(60, 64, 74))   # darker ground for rerolls\nDARKV = dict(',1)
t=t.replace('return {"DarkV": DARKV, "DarkC": DARKC}.get(k) or PAL[k]','return {"DarkV": DARKV, "DarkC": DARKC, "MetalD": METALD}.get(k) or PAL[k]')
b.write_text(t)
q=pathlib.Path('batch.py'); u=q.read_text()
u=u.replace('"thorn_lash", "spore_cloud"]   # producer','"thorn_lash", "spore_cloud", "sunder", "shrapnel_burst"]   # producer')
u=u.replace('for k in ("granite_bulwark", "stone_challenge", "tectonic_shove", "stoneskin")})','for k in ("granite_bulwark", "stone_challenge", "tectonic_shove", "stoneskin", "sunder", "shrapnel_burst")})')
q.write_text(u)
o=pathlib.Path('../work/overrides.json'); ov=json.loads(o.read_text()); ov["iron_crush"]=6019; o.write_text(json.dumps(ov))
