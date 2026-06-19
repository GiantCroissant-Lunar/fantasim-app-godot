#!/usr/bin/env python3
"""comfy-worker — text -> 3D MESH via a real ComfyUI instance.

Runs a combined workflow (SDXL txt2img -> TripoSR) in one /prompt submission and
returns the generated mesh (.obj) plus the intermediate preview image. On Apple
Silicon TripoSR yields an UNTEXTURED mesh (no CUDA rasterizer) — that's fine,
Blender owns texturing/cleanup downstream.

Workflow-as-data: workflows/text_to_mesh.api.json, params injected by node id.
Degrades to a stub (no mesh) if ComfyUI is unreachable so the pipeline stays green.

env: COMFY_URL (default http://127.0.0.1:8188), COMFY_CKPT (optional),
COMFY_WORKFLOW (default text_to_mesh.api.json).
"""
import asyncio
import json
import os
import threading
import time
import urllib.parse
import urllib.request
import uuid
from pathlib import Path

from iii import register_worker

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
ARTIFACTS = ROOT / "build" / "_artifacts" / "generated"
COMFY_URL = os.environ.get("COMFY_URL", "http://127.0.0.1:8188").rstrip("/")
WORKFLOW = Path(os.environ.get("COMFY_WORKFLOW", HERE / "workflows" / "text_to_mesh.api.json"))
III_URL = os.environ.get("III_URL", "ws://127.0.0.1:49134")

# Nudges SDXL toward a clean, TripoSR-friendly single object.
PROMPT_SUFFIX = ", single centered object, plain background, full view, product shot"

client = register_worker(III_URL)


def _http(url, data=None, timeout=30):
    body = json.dumps(data).encode() if data is not None else None
    req = urllib.request.Request(url, data=body, headers={"Content-Type": "application/json"} if body else {})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return r.read()


def _http_json(url, data=None, timeout=30):
    return json.loads(_http(url, data, timeout))


def _reachable():
    try:
        _http(f"{COMFY_URL}/system_stats", timeout=3)
        return True
    except Exception:
        return False


def _pick_ckpt():
    if os.environ.get("COMFY_CKPT"):
        return os.environ["COMFY_CKPT"]
    oi = _http_json(f"{COMFY_URL}/object_info/CheckpointLoaderSimple", timeout=10)
    names = oi["CheckpointLoaderSimple"]["input"]["required"]["ckpt_name"][0]
    # prefer an SD/SDXL checkpoint, not the TripoSR weights
    for n in names:
        if "tripo" not in n.lower():
            return n
    return names[0]


def _fetch(file_info: dict, dest: Path):
    q = urllib.parse.urlencode({
        "filename": file_info["filename"],
        "subfolder": file_info.get("subfolder", ""),
        "type": file_info.get("type", "output"),
    })
    dest.write_bytes(_http(f"{COMFY_URL}/view?{q}", timeout=120))
    return str(dest)


def _run(prompt: str, out_dir: Path) -> dict:
    wf = json.loads(WORKFLOW.read_text())
    wf["6"]["inputs"]["text"] = prompt + PROMPT_SUFFIX
    wf["3"]["inputs"]["seed"] = int(uuid.uuid4().int % (2**31))
    wf["4"]["inputs"]["ckpt_name"] = _pick_ckpt()

    pid = _http_json(f"{COMFY_URL}/prompt", {"prompt": wf, "client_id": uuid.uuid4().hex})["prompt_id"]
    for _ in range(600):
        node = _http_json(f"{COMFY_URL}/history/{pid}", timeout=10).get(pid, {})
        if node.get("status", {}).get("status_str") == "error":
            raise RuntimeError(f"ComfyUI error: {json.dumps(node.get('status'))[:300]}")
        if node.get("outputs"):
            mesh = image = None
            for out in node["outputs"].values():
                if out.get("mesh"):
                    mesh = _fetch(out["mesh"][0], out_dir / "model.obj")
                if out.get("images"):
                    image = _fetch(out["images"][0], out_dir / "comfy.png")
            if mesh:
                return {"mesh": mesh, "image": image}
        time.sleep(1)
    raise RuntimeError("ComfyUI timed out without producing a mesh")


async def generate(payload):
    payload = payload or {}
    prompt = payload.get("prompt", "")
    job_id = payload.get("job_id", "job")
    out_dir = ARTIFACTS / job_id
    out_dir.mkdir(parents=True, exist_ok=True)

    if not _reachable():
        stub = out_dir / "comfy_stub.txt"
        stub.write_text(f"[fallback: ComfyUI unreachable @ {COMFY_URL}] prompt={prompt!r}\n")
        return {"path": str(stub), "mesh": None, "image": None, "fallback": True, "job_id": job_id}

    res = await asyncio.to_thread(_run, prompt, out_dir)
    # `path` is the mesh — blender.refine imports it
    return {"path": res["mesh"], "mesh": res["mesh"], "image": res["image"], "fallback": False, "job_id": job_id}


client.register_function("comfy.generate", generate)
print(f"comfy-worker: registered comfy.generate (text->mesh) @ {III_URL} (comfy={COMFY_URL})", flush=True)
threading.Event().wait()
