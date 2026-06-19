#!/usr/bin/env python3
"""blender-worker — two headless-Blender functions:

  blender.refine   : TripoSR .obj -> game-ready mesh -> exports USD (.usdc)
  asset.to_gltf    : any supported file (USD/FBX/OBJ/glTF) -> glTF

`asset.to_gltf` is the format-agnostic parser the Godot app delegates to, so the
app only ever loads glTF. Blocking Blender runs go through asyncio.to_thread so
the iii event loop stays responsive.
"""
import asyncio
import os
import subprocess
import threading
from pathlib import Path

from iii import register_worker

ROOT = Path(__file__).resolve().parents[3]
ARTIFACTS = ROOT / "build" / "_artifacts" / "generated"
HERE = Path(__file__).resolve().parent
REFINE = HERE / "refine.py"
CONVERT = HERE / "convert.py"
III_URL = os.environ.get("III_URL", "ws://127.0.0.1:49134")
BLENDER = os.environ.get("BLENDER_BIN", "/Applications/Blender.app/Contents/MacOS/Blender")

client = register_worker(III_URL)


async def _blender(script: Path, out_path: str, src: str, timeout=180):
    proc = await asyncio.to_thread(
        subprocess.run,
        [BLENDER, "--background", "--python", str(script), "--", out_path, src],
        capture_output=True, text=True, timeout=timeout,
    )
    if not os.path.exists(out_path):
        raise RuntimeError(f"blender {script.name} failed (rc={proc.returncode}): {proc.stderr[-500:]}")
    return out_path


async def refine(payload):
    payload = payload or {}
    job_id = payload.get("job_id", "job")
    source = payload.get("source", "")
    out_dir = ARTIFACTS / job_id
    out_dir.mkdir(parents=True, exist_ok=True)
    usd = await _blender(REFINE, str(out_dir / "model.usdc"), source)
    return {"usd_path": usd, "job_id": job_id}


async def to_gltf(payload):
    payload = payload or {}
    job_id = payload.get("job_id", "job")
    source = payload.get("source", "")
    out_dir = ARTIFACTS / job_id
    out_dir.mkdir(parents=True, exist_ok=True)
    glb = await _blender(CONVERT, str(out_dir / "model.glb"), source)
    return {"glb_path": glb, "source": source, "job_id": job_id}


client.register_function("blender.refine", refine)
client.register_function("asset.to_gltf", to_gltf)
print(f"blender-worker: registered blender.refine + asset.to_gltf @ {III_URL}", flush=True)
threading.Event().wait()
