"""Meshy API CLI: local-only image-to-3D generation and task retrieval (standard library only -- urllib,
json, base64, argparse, time, hashlib, winreg; no extra deps). Matches ArtLab's conventions (see
../README.md): paths never hard-coded to a user's home, everything driven by args/env. CI never runs this
(it needs a paid MESHY_API_KEY and, for `image-to-3d --yes`, spends real credits).

Usage:
  python meshy.py balance
  python meshy.py image-to-3d --image IN.png --out DIR [--polycount N] [--symmetry auto|on|off]
                               [--texture/--no-texture] [--texture-resolution 2k|4k|8k] [--pbr]
                               [--model AI_MODEL] [--dry-run] [--yes] [--timeout SECONDS]
  python meshy.py get --task TASK_ID --out DIR
  python meshy.py remesh --task INPUT_TASK_ID --out DIR [--target-polycount N] [--topology quad|triangle]
                          [--formats glb,fbx,obj,usdz,blend,stl,3mf] [--dry-run] [--yes] [--timeout SECONDS]

Docs consulted (fetched 2026-09-30 -- cite exact fields/paths from these pages, not from memory):
  https://docs.meshy.ai/en/api/authentication  Authorization: Bearer <key>; 401 on a bad/missing key
  https://docs.meshy.ai/en/api/image-to-3d     POST .../image-to-3d (create), GET .../image-to-3d/:id (retrieve)
  https://docs.meshy.ai/en/api/remesh          POST .../remesh (create), GET .../remesh/:id (retrieve)
  https://docs.meshy.ai/en/api/balance         GET .../balance -> {"balance": N}
  https://docs.meshy.ai/en/api/pricing         credit cost table (image-to-3d block, remesh: 5 credits flat)
  https://docs.meshy.ai/en/api/errors          {"message": ...} / task_error shape, HTTP status codes

Endpoints (https://api.meshy.ai):
  GET  /openapi/v1/balance                 free, any number of times
  POST /openapi/v1/image-to-3d             creates a task; costs credits -- spend-guarded below
  GET  /openapi/v1/image-to-3d/{id}        free, any number of times (task status, then the download URLs)
  POST /openapi/v1/remesh                  creates a remesh task from an existing task_id or model_url;
                                            5 credits flat (docs.meshy.ai/en/api/pricing) -- spend-guarded below
  GET  /openapi/v1/remesh/{id}             free, any number of times (task status, then the download URLs)

SPEND GUARD: `image-to-3d` and `remesh` always build the request and print the exact body (key redacted,
image data truncated for image-to-3d) plus the documented credit cost, then stop. Nothing reaches Meshy
unless you also pass --yes. --dry-run never sends a request even together with --yes, for eyeballing the
request shape with zero network risk.

remesh notes (docs.meshy.ai/en/api/remesh, read 2026-09-30):
  - input_task_id (the completed Image to 3D / Text to 3D task to remesh) and model_url (a public URL or
    data URI to an existing model) are alternatives -- this CLI's `remesh` subcommand only exposes
    input_task_id (--task), the case this repo actually needs (re-topologising an existing Meshy task's
    output), not a from-scratch model_url upload.
  - target_polycount: integer, default 30000, documented range 100-300,000 (may be tier-limited).
  - topology: "quad" (quad-dominant) or "triangle" (decimated triangle mesh, the default).
  - target_formats: list, default ["glb"] if omitted; available values glb/fbx/obj/usdz/blend/stl/3mf.
  - Texture handling on remesh is NOT documented either way: the remesh request body has no
    should_texture/texture_resolution/texture_prompt fields, and the retrieve-task response documents
    model_urls + thumbnail_url but no texture_urls (unlike image-to-3d's retrieve response, which has
    both). Whether the output GLB/FBX/etc. embeds a re-mapped copy of the original texture, or comes back
    untextured, is not stated on the page as of 2026-09-30 -- verify by inspecting a real remeshed output
    (e.g. with gltf_inspect.py) before relying on it, or ask Meshy support.

Key handling: MESHY_API_KEY is read from the process environment first; if unset, falls back to the
Windows **user**-level environment variable at HKCU\\Environment\\MESHY_API_KEY via winreg (read-only,
never written) -- a shell opened before `setx MESHY_API_KEY ...` (or the Environment Variables control
panel) ran won't see the new value in its process environment until restarted, and this fallback covers
that case. The key is never printed, logged, written to task.json or written to a provenance file; see
redact() below, applied to every piece of text this script prints or persists that could echo the request.

Credit cost (https://docs.meshy.ai/en/api/pricing, "Image to 3D Pricing" table, read 2026-09-30):
  model            mesh only   +2K texture   +4K texture   +8K texture
  meshy-7.1        20          30            30            35   (+5 if geometry_resolution is 2k/4k,
                                                                   the "ultra geometry" surcharge -- this
                                                                   CLI never sets geometry_resolution, so
                                                                   it never applies here)
  meshy-6          20          30            30            35
  meshy-6-lite      5          15            --            --   (no 4K/8K texture tier at all)
  meshy-t2          5          15            15            20
  ai_model="latest" (this CLI's default, and Meshy's own documented default) tracks whichever model Meshy
  currently calls "latest"; this script prints the meshy-7.1 price for it and SAYS it is an assumption --
  pass --model explicitly (e.g. meshy-6-lite for a cheap check) to pin and correctly price a specific model.
  enable_pbr (--pbr) has no separate line item on the pricing page as of this writing; the printed cost
  does not add anything for it -- compare `balance` before and after a real spend if that matters to you.

Differences from what we assumed going in (see docs/spikes/055-3d-mini-spike.md and
Tooling/ArtLab/provenance/spike55-griffin-meshy.md for the mini-spike this tool follows up on):
  - Texturing is NOT a separate call/step. `should_texture` (default true) and `texture_resolution` are
    plain fields on the same image-to-3d request, resolved in the same task as the mesh. (Retexturing an
    *existing* mesh with a new prompt/image is a different, separately priced endpoint this CLI doesn't use.)
  - `symmetry_mode` (this CLI's --symmetry) is listed as **deprecated** in the current docs, alongside
    ultra_mode, hd_texture and is_a_t_pose. It is still accepted by the API (kept here for parity with
    older examples/scripts) but the docs do not promise it still does anything; a warning prints if you use it.
  - target_polycount only takes effect together with should_remesh: true on the default (standard) model
    type -- setting target_polycount alone does not trigger remeshing. This CLI sets should_remesh: true
    whenever --polycount is given.
  - The retrieve-task response's documented fields do NOT include the original image_url or request
    settings (only id, type, status, progress, model_urls, thumbnail_url(s), texture_urls, texture_prompt,
    texture_image_url, geometry_resolution, timestamps, preceding_tasks, consumed_credits, task_error) --
    so `get` (a bare re-download against an old task id) cannot recover the exact original request, and the
    provenance note it writes says so explicitly.
  - `expires_at` (a millisecond timestamp in the task response) is documented as when the *task result*
    expires; the docs do not spell out a fixed retention period in plain words (e.g. "14 days") for the
    download links themselves. Download promptly; task.json (which this CLI writes) keeps expires_at.
  - target_formats (glb/obj/fbx/stl/usdz/3mf) is a real request field; this CLI does not set it, so the
    server's own default applies -- it just downloads whichever keys `model_urls` comes back with, so a
    future default-format change on Meshy's side is picked up automatically rather than silently ignored.
"""
import argparse
import base64
import hashlib
import json
import os
import pathlib
import sys
import time
import urllib.error
import urllib.request

API_ROOT = "https://api.meshy.ai"
REDACTED = "***REDACTED***"

# Credit cost, https://docs.meshy.ai/en/api/pricing ("Image to 3D Pricing" table), read 2026-09-30.
# Keys per texture tier are the credit cost for "mesh + that texture resolution"; "mesh" is mesh-only.
IMAGE_TO_3D_COSTS = {
    "meshy-7.1": {"mesh": 20, "2k": 30, "4k": 30, "8k": 35, "ultra_surcharge": 5},
    "meshy-6": {"mesh": 20, "2k": 30, "4k": 30, "8k": 35},
    "meshy-6-lite": {"mesh": 5, "2k": 15, "4k": None, "8k": None},
    "meshy-t2": {"mesh": 5, "2k": 15, "4k": 15, "8k": 20},
}
DEPRECATED_FIELDS = ("symmetry_mode", "ultra_mode", "hd_texture", "is_a_t_pose")
IMAGE_MIME = {".png": "image/png", ".jpg": "image/jpeg", ".jpeg": "image/jpeg"}

# https://docs.meshy.ai/en/api/pricing ("Remesh" row), read 2026-09-30: flat 5 credits per request,
# regardless of target_polycount or topology (the pricing table shows no conditional pricing for either).
REMESH_COST = 5
REMESH_TOPOLOGIES = ("quad", "triangle")
REMESH_FORMATS = ("glb", "fbx", "obj", "usdz", "blend", "stl", "3mf")


class MeshyError(Exception):
    """An HTTP/API-level error. status is 0 for a connection-level failure (no HTTP response)."""

    def __init__(self, status, message, payload=None):
        super().__init__(f"HTTP {status}: {message}" if status else message)
        self.status = status
        self.message = message
        self.payload = payload or {}


# ---------------------------------------------------------------------------------------------------------
# Pure helpers (no network, no filesystem beyond reading the one image) -- see test_meshy.py.
# ---------------------------------------------------------------------------------------------------------

def redact(text, key):
    """Strip an API key out of arbitrary text before it is printed, logged or written to disk."""
    text = text if isinstance(text, str) else str(text)
    if key:
        text = text.replace(key, REDACTED)
    return text


def image_to_data_uri(path):
    """Read a local image and return (data_uri, byte_count). Meshy's image_url field accepts either a
    public URL or a base64 data URI (https://docs.meshy.ai/en/api/image-to-3d)."""
    path = pathlib.Path(path)
    mime = IMAGE_MIME.get(path.suffix.lower())
    if mime is None:
        raise ValueError(f"unsupported image type {path.suffix!r}; Meshy's image-to-3d accepts .png, .jpg, .jpeg")
    data = path.read_bytes()
    return f"data:{mime};base64,{base64.b64encode(data).decode('ascii')}", len(data)


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def resolve_model_for_cost(model):
    """Returns (table_key, assumed). 'latest' isn't in the priced table by name -- we assume it currently
    prices like meshy-7.1 (the newest model at time of writing) and flag that assumption."""
    if model in IMAGE_TO_3D_COSTS:
        return model, False
    return "meshy-7.1", True


def cost_lookup(model, should_texture, texture_resolution, pbr):
    """Returns (credits: int|None, note: str). credits is None when the docs' pricing table has no entry
    for the requested combination (e.g. 8K texture on meshy-6-lite)."""
    key, assumed = resolve_model_for_cost(model)
    table = IMAGE_TO_3D_COSTS[key]
    if not should_texture:
        credits, note = table["mesh"], "mesh only, no texture"
    else:
        res = texture_resolution.lower()
        credits = table.get(res)
        note = f"mesh + {texture_resolution} texture"
        if credits is None:
            return None, (f"{key} has no documented {texture_resolution} texture tier "
                           f"(see https://docs.meshy.ai/en/api/pricing) -- choose 2k or 4k, or use a different model")
    if pbr:
        note += "; enable_pbr requested (no separate line item on the pricing page as of 2026-09-30 -- not added)"
    if assumed:
        note = (f"ASSUMED: ai_model={model!r} priced as {key} (the newest model at time of writing; "
                 f"pass --model to pin a specific, correctly-priced model) -- {note}")
    return credits, note


def build_image_to_3d_body(image_uri, model, polycount, symmetry, texture, texture_resolution, pbr):
    body = {
        "image_url": image_uri,
        "ai_model": model,
        "should_texture": texture,
        "enable_pbr": pbr,
    }
    if texture:
        body["texture_resolution"] = texture_resolution
    if polycount is not None:
        body["target_polycount"] = polycount
        body["should_remesh"] = True  # target_polycount only applies with should_remesh: true (standard model_type)
    if symmetry is not None:
        body["symmetry_mode"] = symmetry  # deprecated field, kept only because the caller asked for it; see docstring
    return body


def build_remesh_body(input_task_id, target_polycount, topology, target_formats):
    """https://docs.meshy.ai/en/api/remesh -- input_task_id is the completed Image to 3D / Text to 3D task
    to remesh (this CLI's `remesh` subcommand doesn't expose the model_url alternative). target_polycount
    and topology are omitted when None/unset so Meshy's own documented defaults (30000, "triangle") apply;
    target_formats is only included when the caller asked for something other than the default (["glb"]
    when omitted, per the docs)."""
    body = {"input_task_id": input_task_id}
    if target_polycount is not None:
        body["target_polycount"] = target_polycount
    if topology is not None:
        body["topology"] = topology
    if target_formats:
        body["target_formats"] = list(target_formats)
    return body


# ---------------------------------------------------------------------------------------------------------
# Key handling
# ---------------------------------------------------------------------------------------------------------

def _read_user_env_var(name):
    """Windows user-level environment variable (HKCU\\Environment), read-only. A shell started before
    `setx` / the Environment Variables control panel wrote this won't see it in os.environ until restarted."""
    if sys.platform != "win32":
        return None
    try:
        import winreg
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Environment") as k:
            value, _ = winreg.QueryValueEx(k, name)
            return value or None
    except OSError:
        return None


def get_api_key():
    """Returns (key, source) or (None, None). Never prints the key itself."""
    key = os.environ.get("MESHY_API_KEY")
    if key:
        return key, "process environment"
    key = _read_user_env_var("MESHY_API_KEY")
    if key:
        return key, "Windows user environment (HKCU\\Environment) -- restart your shell to see it automatically"
    return None, None


# ---------------------------------------------------------------------------------------------------------
# HTTP
# ---------------------------------------------------------------------------------------------------------

def api_request(method, path, key, body=None, timeout=60):
    url = f"{API_ROOT}{path}"
    headers = {"Authorization": f"Bearer {key}"}
    data = None
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            raw = resp.read()
            return resp.status, (json.loads(raw) if raw else {})
    except urllib.error.HTTPError as e:
        raw = e.read()
        try:
            payload = json.loads(raw)
        except (json.JSONDecodeError, UnicodeDecodeError):
            payload = {"message": raw.decode("utf-8", "replace")}
        message = payload.get("message") or (payload.get("task_error") or {}).get("message") or json.dumps(payload)
        raise MeshyError(e.code, redact(message, key), payload) from None
    except urllib.error.URLError as e:
        raise MeshyError(0, redact(str(e.reason), key), {}) from None
    except TimeoutError as e:
        raise MeshyError(0, redact(f"timed out after {timeout}s: {e}", key), {}) from None


def download_file(url, dest, timeout=120):
    dest.parent.mkdir(parents=True, exist_ok=True)
    req = urllib.request.Request(url, headers={"User-Agent": "beast-craft-artlab-meshy-cli"})
    with urllib.request.urlopen(req, timeout=timeout) as resp, open(dest, "wb") as f:
        while True:
            chunk = resp.read(1 << 16)
            if not chunk:
                break
            f.write(chunk)
    return dest.stat().st_size


# ---------------------------------------------------------------------------------------------------------
# Task polling + output saving (shared by image-to-3d --yes and get)
# ---------------------------------------------------------------------------------------------------------

TERMINAL_STATUSES = ("SUCCEEDED", "FAILED", "CANCELED")


def poll_task(task_id, key, timeout_s, endpoint="/openapi/v1/image-to-3d"):
    """GET {endpoint}/:id on a capped exponential backoff until a terminal status or timeout. Free per the
    docs (no credit cost for retrieval), so polling aggressively is not a spend risk. endpoint defaults to
    image-to-3d; `remesh` passes /openapi/v1/remesh (https://docs.meshy.ai/en/api/remesh)."""
    start = time.monotonic()
    delay = 2.0
    while True:
        _, task = api_request("GET", f"{endpoint}/{task_id}", key)
        status = task.get("status", "?")
        progress = task.get("progress", "?")
        elapsed = time.monotonic() - start
        print(f"  [{elapsed:6.1f}s] status={status} progress={progress}")
        if status in TERMINAL_STATUSES:
            return task
        if elapsed >= timeout_s:
            raise MeshyError(0, f"timed out after {timeout_s}s waiting for task {task_id} "
                                 f"(last status={status} progress={progress})")
        time.sleep(delay)
        delay = min(delay * 1.5, 15.0)


def save_task_outputs(task, out_dir):
    """Downloads every model format, texture map and thumbnail the task response provides, and writes
    task.json (the raw response; it carries no API key). Returns the list of (label, path, bytes) saved."""
    out_dir = pathlib.Path(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    saved = []

    for fmt, url in (task.get("model_urls") or {}).items():
        if not url:
            continue
        dest = out_dir / f"model.{fmt}"
        size = download_file(url, dest)
        saved.append((f"model_urls.{fmt}", dest, size))

    for i, tex in enumerate(task.get("texture_urls") or []):
        for map_name, url in (tex or {}).items():
            if not url:
                continue
            ext = pathlib.Path(url.split("?", 1)[0]).suffix or ".png"
            dest = out_dir / "textures" / f"{i}_{map_name}{ext}"
            size = download_file(url, dest)
            saved.append((f"texture_urls[{i}].{map_name}", dest, size))

    thumb = task.get("thumbnail_url")
    if thumb:
        ext = pathlib.Path(thumb.split("?", 1)[0]).suffix or ".png"
        dest = out_dir / f"thumbnail{ext}"
        size = download_file(thumb, dest)
        saved.append(("thumbnail_url", dest, size))

    alpha_thumb = task.get("alpha_thumbnail_url")
    if alpha_thumb:
        ext = pathlib.Path(alpha_thumb.split("?", 1)[0]).suffix or ".png"
        dest = out_dir / f"thumbnail_alpha{ext}"
        size = download_file(alpha_thumb, dest)
        saved.append(("alpha_thumbnail_url", dest, size))

    for view, url in (task.get("thumbnail_urls") or {}).items():
        if not url:
            continue
        ext = pathlib.Path(url.split("?", 1)[0]).suffix or ".png"
        dest = out_dir / f"thumbnail_{view}{ext}"
        size = download_file(url, dest)
        saved.append((f"thumbnail_urls.{view}", dest, size))

    (out_dir / "task.json").write_text(json.dumps(task, indent=2), encoding="utf-8")
    return saved


def repo_root():
    return pathlib.Path(__file__).resolve().parents[3]


def write_provenance(out_dir, task, request_info=None, provenance_path=None):
    """Writes/updates a provenance markdown record in the pattern of Tooling/ArtLab/provenance/
    (see spike55-griffin-meshy.md): source image + SHA-256, task id, model, settings, date, credits spent,
    and the paid-tier licence note. request_info is None when called from `get` (a bare re-download), in
    which case the record says plainly that the original request settings are not recoverable from the
    task-retrieval API (its documented response does not echo image_url or the request fields back)."""
    provenance_path = pathlib.Path(provenance_path) if provenance_path else (
        pathlib.Path(__file__).resolve().parent.parent / "provenance" / f"meshy-{task.get('id', 'unknown')}.md")
    provenance_path.parent.mkdir(parents=True, exist_ok=True)

    lines = [f"# Provenance: Meshy image-to-3D task `{task.get('id', 'unknown')}`", ""]
    lines.append("**Status:** local-tooling output of `Tooling/ArtLab/scripts/meshy.py`, not shipped in the "
                  "game unless a producer later approves it as in-game art (see `docs/art/art-brief.md`'s "
                  "AI-disclosure rule).")
    lines.append("")
    lines.append("| | |")
    lines.append("| --- | --- |")
    lines.append("| Tool | Meshy, **paid tier**, image-to-3D (`POST /openapi/v1/image-to-3d`) |")
    if request_info:
        src = request_info.get("source_image_repo_relative") or request_info["source_image"]
        lines.append(f"| Input | `{src}` -- SHA-256 `{request_info['source_sha256']}` |")
        lines.append(f"| ai_model | `{request_info['model']}` |")
        settings = {k: v for k, v in request_info.get("body", {}).items() if k != "image_url"}
        lines.append(f"| Settings | `{json.dumps(settings)}` |")
    else:
        lines.append("| Input | not recoverable from the task-retrieval API (re-downloaded via `meshy.py get`; "
                      "the documented retrieve-task response does not echo the original image_url or request "
                      "settings) -- see the provenance entry written when the task was created, if any |")
    lines.append(f"| Task id | `{task.get('id', 'unknown')}` |")
    lines.append(f"| Status | `{task.get('status', 'unknown')}` |")
    lines.append(f"| Credits spent | `{task.get('consumed_credits', 'unknown')}` |")
    created = task.get("created_at")
    lines.append(f"| Generated | {time.strftime('%Y-%m-%d', time.gmtime(created / 1000)) if created else 'unknown'} |")
    lines.append("| Output ownership | Meshy's paid tier grants the generating account ownership of outputs "
                  "(see Meshy's terms at generation time); unlike the free tier (CC BY, per "
                  "`docs/design/content-bible.md`), no attribution requirement applies |")
    lines.append("")
    lines.append("## Notes")
    lines.append("")
    lines.append("- Generated with `Tooling/ArtLab/scripts/meshy.py` (local-only CLI; CI never runs it). "
                  "The model is a tool only, per `docs/art/art-brief.md` section 0: it does not ship with "
                  "the game unless separately approved as a final, and nothing here changes the licence "
                  "position of the ten beasts already in the game (made by the local `Tooling/ArtLab` "
                  "diffusion pipeline, unrelated to Meshy).")
    if task.get("expires_at"):
        lines.append(f"- `expires_at` on the task (ms since epoch): `{task['expires_at']}` -- the docs describe "
                      "this as when the task result expires; no separate fixed retention period for the "
                      "download links is documented in plain words, so outputs were downloaded immediately "
                      "rather than relied on being re-fetchable later.")
    lines.append("")

    provenance_path.write_text("\n".join(lines), encoding="utf-8")
    return provenance_path


# ---------------------------------------------------------------------------------------------------------
# Subcommands
# ---------------------------------------------------------------------------------------------------------

def cmd_balance(args):
    key, source = get_api_key()
    if not key:
        print("MESHY_API_KEY not found in the process environment or the Windows user environment.", file=sys.stderr)
        return 2
    try:
        _, payload = api_request("GET", "/openapi/v1/balance", key)
    except MeshyError as e:
        print(f"balance request failed: {e}", file=sys.stderr)
        return 1
    print(f"balance: {payload.get('balance')} credits  (key from: {source})")
    return 0


def cmd_image_to_3d(args):
    image_path = pathlib.Path(args.image)
    if not image_path.is_file():
        print(f"no such image: {image_path}", file=sys.stderr)
        return 2
    if args.texture_resolution and args.texture_resolution.lower() not in ("2k", "4k", "8k"):
        print(f"--texture-resolution must be 2k, 4k or 8k, got {args.texture_resolution!r}", file=sys.stderr)
        return 2

    out_dir = pathlib.Path(args.out) if args.out else None
    if not args.dry_run and out_dir is None:
        print("--out DIR is required (or set ARTLAB_MESHY_OUT) unless --dry-run", file=sys.stderr)
        return 2

    key, source = (None, None)
    if not args.dry_run:
        key, source = get_api_key()
        if not key:
            print("MESHY_API_KEY not found in the process environment or the Windows user environment.",
                  file=sys.stderr)
            return 2

    image_uri, byte_count = image_to_data_uri(image_path)
    body = build_image_to_3d_body(image_uri, args.model, args.polycount, args.symmetry,
                                   args.texture, args.texture_resolution, args.pbr)
    credits, cost_note = cost_lookup(args.model, args.texture, args.texture_resolution, args.pbr)

    if args.symmetry is not None:
        print(f"note: --symmetry sets the deprecated symmetry_mode field (see this script's docstring); "
              f"it may have no effect.", file=sys.stderr)

    printable_body = dict(body)
    printable_body["image_url"] = f"data:{IMAGE_MIME[image_path.suffix.lower()]};base64,<{byte_count:,} bytes, " \
                                    f"{len(image_uri):,} chars, omitted>"
    print("POST https://api.meshy.ai/openapi/v1/image-to-3d")
    print("Authorization: Bearer " + REDACTED)
    print(json.dumps(printable_body, indent=2))
    print()
    if credits is None:
        print(f"credit cost: UNKNOWN -- {cost_note}", file=sys.stderr)
    else:
        print(f"credit cost: {credits} credits ({cost_note})")

    if args.dry_run:
        print("\n--dry-run: nothing sent.")
        return 0
    if not args.yes:
        print("\nSpend guard: pass --yes to actually submit this request and spend credits.")
        return 0
    if credits is None:
        print("\nrefusing to submit: credit cost could not be determined from the documented pricing table "
              "(see the note above) -- pick a model/resolution combination the docs price, or check "
              "https://docs.meshy.ai/en/api/pricing yourself first.", file=sys.stderr)
        return 1

    try:
        _, created = api_request("POST", "/openapi/v1/image-to-3d", key, body=body)
        task_id = created.get("result")
        if not task_id:
            print(f"unexpected response (no 'result' task id): {json.dumps(created)}", file=sys.stderr)
            return 1
        print(f"\nsubmitted: task {task_id}")
        task = poll_task(task_id, key, args.timeout)
    except MeshyError as e:
        print(f"\nimage-to-3d failed: {e}", file=sys.stderr)
        return 1

    if task.get("status") != "SUCCEEDED":
        err = task.get("task_error") or {}
        print(f"\ntask did not succeed: status={task.get('status')} "
              f"{redact(err.get('message', ''), key)}", file=sys.stderr)
        (out_dir / "task.json").parent.mkdir(parents=True, exist_ok=True)
        (out_dir / "task.json").write_text(json.dumps(task, indent=2), encoding="utf-8")
        return 1

    saved = save_task_outputs(task, out_dir)
    print(f"\nsaved {len(saved)} file(s) to {out_dir}:")
    for label, path, size in saved:
        print(f"  {label:28s} {path}  ({size:,} bytes)")
    print(f"  {'task.json':28s} {out_dir / 'task.json'}")

    try:
        repo_relative = str(image_path.resolve().relative_to(repo_root()))
    except ValueError:
        repo_relative = None
    request_info = {
        "source_image": str(image_path),
        "source_image_repo_relative": repo_relative,
        "source_sha256": sha256_file(image_path),
        "model": args.model,
        "body": body,
    }
    prov_path = write_provenance(out_dir, task, request_info=request_info)
    print(f"\nprovenance written: {prov_path}")
    return 0


def cmd_get(args):
    out_dir = pathlib.Path(args.out) if args.out else None
    if out_dir is None:
        print("--out DIR is required (or set ARTLAB_MESHY_OUT)", file=sys.stderr)
        return 2
    key, source = get_api_key()
    if not key:
        print("MESHY_API_KEY not found in the process environment or the Windows user environment.", file=sys.stderr)
        return 2
    try:
        _, task = api_request("GET", f"/openapi/v1/image-to-3d/{args.task}", key)
    except MeshyError as e:
        print(f"get failed: {e}", file=sys.stderr)
        return 1
    if task.get("status") != "SUCCEEDED":
        print(f"task {args.task} is not SUCCEEDED yet (status={task.get('status')}); nothing to download.",
              file=sys.stderr)
        return 1
    saved = save_task_outputs(task, out_dir)
    print(f"saved {len(saved)} file(s) to {out_dir}:")
    for label, path, size in saved:
        print(f"  {label:28s} {path}  ({size:,} bytes)")
    prov_path = write_provenance(out_dir, task, request_info=None)
    print(f"provenance written: {prov_path}")
    return 0


def cmd_remesh(args):
    if args.target_polycount is not None and not (100 <= args.target_polycount <= 300000):
        print(f"--target-polycount must be 100-300000 (docs.meshy.ai/en/api/remesh), got {args.target_polycount}",
              file=sys.stderr)
        return 2
    formats = [f.strip().lower() for f in args.formats.split(",")] if args.formats else []
    bad_formats = [f for f in formats if f not in REMESH_FORMATS]
    if bad_formats:
        print(f"--formats has unrecognised value(s) {bad_formats}; available: {', '.join(REMESH_FORMATS)}",
              file=sys.stderr)
        return 2

    out_dir = pathlib.Path(args.out) if args.out else None
    if not args.dry_run and out_dir is None:
        print("--out DIR is required (or set ARTLAB_MESHY_OUT) unless --dry-run", file=sys.stderr)
        return 2

    key, source = (None, None)
    if not args.dry_run:
        key, source = get_api_key()
        if not key:
            print("MESHY_API_KEY not found in the process environment or the Windows user environment.",
                  file=sys.stderr)
            return 2

    body = build_remesh_body(args.task, args.target_polycount, args.topology, formats)

    print("POST https://api.meshy.ai/openapi/v1/remesh")
    print("Authorization: Bearer " + REDACTED)
    print(json.dumps(body, indent=2))
    print()
    print(f"credit cost: {REMESH_COST} credits (flat rate, https://docs.meshy.ai/en/api/pricing "
          f"-- does not vary with target_polycount or topology per the docs)")
    print("note: whether the remeshed output keeps/re-bakes the input task's texture is not documented "
          "(see this script's docstring) -- inspect the downloaded GLB (e.g. with gltf_inspect.py) once "
          "you have real output before relying on it.")

    if args.dry_run:
        print("\n--dry-run: nothing sent.")
        return 0
    if not args.yes:
        print("\nSpend guard: pass --yes to actually submit this request and spend credits.")
        return 0

    try:
        _, created = api_request("POST", "/openapi/v1/remesh", key, body=body)
        task_id = created.get("result")
        if not task_id:
            print(f"unexpected response (no 'result' task id): {json.dumps(created)}", file=sys.stderr)
            return 1
        print(f"\nsubmitted: task {task_id}")
        task = poll_task(task_id, key, args.timeout, endpoint="/openapi/v1/remesh")
    except MeshyError as e:
        print(f"\nremesh failed: {e}", file=sys.stderr)
        return 1

    if task.get("status") != "SUCCEEDED":
        err = task.get("task_error") or {}
        print(f"\ntask did not succeed: status={task.get('status')} "
              f"{redact(err.get('message', ''), key)}", file=sys.stderr)
        (out_dir / "task.json").parent.mkdir(parents=True, exist_ok=True)
        (out_dir / "task.json").write_text(json.dumps(task, indent=2), encoding="utf-8")
        return 1

    saved = save_task_outputs(task, out_dir)
    print(f"\nsaved {len(saved)} file(s) to {out_dir}:")
    for label, path, size in saved:
        print(f"  {label:28s} {path}  ({size:,} bytes)")
    print(f"  {'task.json':28s} {out_dir / 'task.json'}")
    return 0


# ---------------------------------------------------------------------------------------------------------

def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)

    sub.add_parser("balance", help="GET /openapi/v1/balance (free)")

    p = sub.add_parser("image-to-3d", help="POST /openapi/v1/image-to-3d then poll and download (spend-guarded)")
    p.add_argument("--image", required=True, help="local .png/.jpg/.jpeg source image")
    p.add_argument("--out", default=os.environ.get("ARTLAB_MESHY_OUT"), help="output dir (or set ARTLAB_MESHY_OUT)")
    p.add_argument("--polycount", type=int, default=None, help="target_polycount; implies should_remesh=true")
    p.add_argument("--symmetry", choices=["auto", "on", "off"], default=None,
                    help="deprecated symmetry_mode field (kept for parity; may have no effect, see docstring)")
    p.add_argument("--texture", dest="texture", action="store_true", default=True, help="should_texture=true (default)")
    p.add_argument("--no-texture", dest="texture", action="store_false", help="should_texture=false (mesh only)")
    p.add_argument("--texture-resolution", default="2k", help="2k (default), 4k or 8k; only used if texturing")
    p.add_argument("--pbr", action="store_true", help="enable_pbr: also generate metallic/roughness/normal maps")
    p.add_argument("--model", default="latest", help="ai_model, e.g. meshy-6-lite, meshy-6, meshy-7.1 (default: latest)")
    p.add_argument("--dry-run", action="store_true", help="print the request and cost; never sends anything")
    p.add_argument("--yes", action="store_true", help="actually submit the request (spends credits)")
    p.add_argument("--timeout", type=float, default=900.0, help="seconds to poll before giving up (default 900)")
    p.set_defaults(func=cmd_image_to_3d)

    p = sub.add_parser("get", help="GET /openapi/v1/image-to-3d/:id and re-download its outputs (free)")
    p.add_argument("--task", required=True, help="task id")
    p.add_argument("--out", default=os.environ.get("ARTLAB_MESHY_OUT"), help="output dir (or set ARTLAB_MESHY_OUT)")
    p.set_defaults(func=cmd_get)

    p = sub.add_parser("remesh", help="POST /openapi/v1/remesh then poll and download (spend-guarded, 5 credits)")
    p.add_argument("--task", required=True, help="input_task_id: a completed Image to 3D / Text to 3D task id")
    p.add_argument("--out", default=os.environ.get("ARTLAB_MESHY_OUT"), help="output dir (or set ARTLAB_MESHY_OUT)")
    p.add_argument("--target-polycount", type=int, default=None,
                    help="target_polycount, 100-300000 (Meshy default 30000 if omitted)")
    p.add_argument("--topology", choices=REMESH_TOPOLOGIES, default=None,
                    help="quad or triangle (Meshy default: triangle if omitted)")
    p.add_argument("--formats", default=None,
                    help=f"comma-separated target_formats, from {{{','.join(REMESH_FORMATS)}}} (default: glb only)")
    p.add_argument("--dry-run", action="store_true", help="print the request and cost; never sends anything")
    p.add_argument("--yes", action="store_true", help="actually submit the request (spends 5 credits)")
    p.add_argument("--timeout", type=float, default=900.0, help="seconds to poll before giving up (default 900)")
    p.set_defaults(func=cmd_remesh)

    args = ap.parse_args(argv)
    if args.cmd == "balance":
        return cmd_balance(args)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
