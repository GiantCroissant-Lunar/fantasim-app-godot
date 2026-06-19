#!/usr/bin/env python3
"""echo-worker — trivial iii function for bridge round-trip tests.

Registers `test.echo`, which returns its payload. Used to verify the gdext
IiiClient bridge round-trips (Godot/C# -> engine -> worker -> response signal)
without depending on ComfyUI/Blender.
"""
import os
import threading

from iii import register_worker

III_URL = os.environ.get("III_URL", "ws://127.0.0.1:49134")
client = register_worker(III_URL)


async def echo(payload):
    return {"echo": payload, "ok": True}


client.register_function("test.echo", echo)
print(f"echo-worker: registered test.echo @ {III_URL}", flush=True)
threading.Event().wait()
