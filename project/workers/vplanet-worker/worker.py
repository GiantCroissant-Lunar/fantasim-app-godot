#!/usr/bin/env python3
"""vplanet-worker - Python iii worker contract slice for VPLanet simulation.
"""
import os
import threading
from iii import register_worker
from vplanet_worker import status, input_build, run, output_parse

III_URL = os.environ.get("III_URL", "ws://127.0.0.1:49134")
client = register_worker(III_URL)

client.register_function("vplanet.status", status)
client.register_function("vplanet.input.build", input_build)
client.register_function("vplanet.run", run)
client.register_function("vplanet.output.parse", output_parse)

print(f"vplanet-worker: registered vplanet.* functions @ {III_URL}", flush=True)
threading.Event().wait()
