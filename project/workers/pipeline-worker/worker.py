#!/usr/bin/env python3
"""pipeline-worker — the coordinator. Owns the text->3D DAG so Godot stays thin:
Godot fires ONE trigger (pipeline.text_to_3d) and gets back a GLB path.

Uses trigger_async (NOT the sync trigger) to call sub-functions from inside an
async handler, so it never blocks its own event loop. iii's OTel-on-wire ties
the whole chain (pipeline -> comfy -> blender) into one trace.
"""
import os
import threading
import uuid

from iii import register_worker

III_URL = os.environ.get("III_URL", "ws://127.0.0.1:49134")
client = register_worker(III_URL)


async def text_to_3d(payload):
    payload = payload or {}
    prompt = payload.get("prompt", "")
    job_id = payload.get("job_id") or uuid.uuid4().hex[:8]

    # 1) text -> 3D mesh (SDXL + TripoSR)
    img = await client.trigger_async({
        "function_id": "comfy.generate",
        "payload": {"prompt": prompt, "job_id": job_id},
        "timeout_ms": 300000,  # first SDXL run loads a 6.5G checkpoint
    })
    # 2) Blender cleans the mesh and exports USD
    usd = await client.trigger_async({
        "function_id": "blender.refine",
        "payload": {"source": img.get("path"), "prompt": prompt, "job_id": job_id},
        "timeout_ms": 180000,
    })
    # 3) delegate USD parsing -> glTF (the app only ever loads glTF)
    glb = await client.trigger_async({
        "function_id": "asset.to_gltf",
        "payload": {"source": usd.get("usd_path"), "job_id": job_id},
        "timeout_ms": 180000,
    })

    return {
        "job_id": job_id,
        "prompt": prompt,
        "comfy_path": img.get("path"),
        "usd_path": usd.get("usd_path"),
        "glb_path": glb.get("glb_path"),
    }


client.register_function("pipeline.text_to_3d", text_to_3d)
print(f"pipeline-worker: registered pipeline.text_to_3d @ {III_URL}", flush=True)
threading.Event().wait()
