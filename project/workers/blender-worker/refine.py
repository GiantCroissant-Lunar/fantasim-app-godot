"""Blender headless refine — run as:
    blender --background --python refine.py -- <out_usd> [input_obj]

Imports the ComfyUI/TripoSR mesh (.obj), makes it game-ready (decimate to a poly
budget, carry TripoSR vertex colors into a material), and exports **USD** (.usdc).
The USD is then parsed back to glTF by the asset.to_gltf converter worker, so the
Godot app never touches USD. Falls back to a cube if no valid mesh is given.
"""
import os
import sys

import bpy

argv = sys.argv
args = argv[argv.index("--") + 1:] if "--" in argv else []
out_usd = args[0] if args else "/tmp/model.usdc"
src = args[1] if len(args) > 1 else ""
FACE_BUDGET = int(os.environ.get("FACE_BUDGET", "40000"))

bpy.ops.wm.read_factory_settings(use_empty=True)

imported = False
if src and os.path.exists(src) and src.lower().endswith(".obj"):
    try:
        bpy.ops.wm.obj_import(filepath=src)
        imported = any(o.type == "MESH" for o in bpy.context.scene.objects)
    except Exception as e:  # noqa: BLE001
        print(f"refine.py: obj import failed: {e}", flush=True)

if imported:
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active

    before = len(obj.data.polygons)
    if before > FACE_BUDGET:
        mod = obj.modifiers.new("decimate", "DECIMATE")
        mod.ratio = FACE_BUDGET / before
        bpy.ops.object.modifier_apply(modifier=mod.name)

    try:
        if obj.data.color_attributes:
            mat = bpy.data.materials.new("triposr")
            mat.use_nodes = True
            nt = mat.node_tree
            bsdf = nt.nodes.get("Principled BSDF")
            vc = nt.nodes.new("ShaderNodeVertexColor")
            vc.layer_name = obj.data.color_attributes[0].name
            nt.links.new(bsdf.inputs["Base Color"], vc.outputs["Color"])
            obj.data.materials.clear()
            obj.data.materials.append(mat)
    except Exception as e:  # noqa: BLE001
        print(f"refine.py: vertex-color material skipped: {e}", flush=True)

    print(f"refine.py: imported {src}, faces {before} -> {len(obj.data.polygons)}", flush=True)
else:
    bpy.ops.mesh.primitive_cube_add(size=1.0)
    print("refine.py: no mesh input -> fallback cube", flush=True)

bpy.ops.wm.usd_export(filepath=out_usd)
print(f"refine.py: exported USD {out_usd}", flush=True)
