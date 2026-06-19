"""Blender headless format converter — run as:
    blender --background --python convert.py -- <out_glb> <source>

Imports <source> (USD/FBX/OBJ/glTF) using Blender's mature importers and exports
glTF. This is the "delegate parsing to the best tool" step: the Godot app stays a
pure glTF consumer, and any format becomes glTF here. Add a format → add a branch.
"""
import os
import sys

import bpy

argv = sys.argv
args = argv[argv.index("--") + 1:] if "--" in argv else []
out_glb = args[0]
src = args[1]
ext = os.path.splitext(src)[1].lower()

bpy.ops.wm.read_factory_settings(use_empty=True)

if ext in (".usd", ".usda", ".usdc", ".usdz"):
    bpy.ops.wm.usd_import(filepath=src)
elif ext == ".fbx":
    bpy.ops.import_scene.fbx(filepath=src)
elif ext == ".obj":
    bpy.ops.wm.obj_import(filepath=src)
elif ext in (".glb", ".gltf"):
    bpy.ops.import_scene.gltf(filepath=src)
else:
    raise SystemExit(f"convert.py: unsupported format {ext}")

bpy.ops.export_scene.gltf(filepath=out_glb, export_format="GLB")
print(f"convert.py: {src} -> {out_glb}", flush=True)
