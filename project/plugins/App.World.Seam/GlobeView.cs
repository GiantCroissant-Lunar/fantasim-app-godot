using System;
using FantaSim.App.World.Dto;
using Godot;

namespace FantaSim.App.World.Seam;

/// <summary>
/// T4 seam: the ONLY App.World tier that touches Godot. Builds a globe <see cref="ArrayMesh"/> from
/// a <see cref="WorldGlobeSnapshot"/> (per-cell base triangle corners + plate id) and rotates each
/// vertex by its plate's Euler rotation on the GPU — a spatial shader driven by a single tick
/// uniform. Scrubbing canonical time (<see cref="SetTick"/>) updates that uniform, so motion is
/// shader-driven with no per-tick CPU mesh rebuild.
/// </summary>
public sealed partial class GlobeView : Node3D
{
    // Spatial shader: rotate each vertex about its plate's Euler axis by (rate * tick); colour by
    // plate id (hue). Plate id rides in UV.x; per-plate (axis.xyz, rate.w) ride in a uniform array.
    private const string ShaderCode = @"
shader_type spatial;
render_mode unshaded, cull_disabled;

uniform float u_tick = 0.0;
uniform int u_plate_count = 1;
uniform vec4 u_plate_axis_rate[16]; // xyz = unit axis, w = radians per canonical tick

varying vec3 v_color;

vec3 rotate_axis(vec3 v, vec3 axis, float angle) {
    float c = cos(angle);
    float s = sin(angle);
    return v * c + cross(axis, v) * s + axis * dot(axis, v) * (1.0 - c);
}

vec3 hue(float h) {
    vec3 k = vec3(1.0, 2.0 / 3.0, 1.0 / 3.0);
    vec3 p = abs(fract(vec3(h) + k) * 6.0 - 3.0);
    return clamp(p - 1.0, 0.0, 1.0);
}

void vertex() {
    int pid = int(UV.x + 0.5);
    vec4 ar = u_plate_axis_rate[pid];
    float angle = ar.w * u_tick;
    VERTEX = rotate_axis(VERTEX, normalize(ar.xyz), angle);
    float denom = max(float(u_plate_count), 1.0);
    v_color = hue(float(pid) / denom) * 0.85 + 0.12;
}

void fragment() {
    ALBEDO = v_color;
}
";

    private readonly WorldGlobeSnapshot _snapshot;
    private ShaderMaterial? _material;
    private double _tick;

    // Verification utility (inert unless FANTASIM_GLOBE_CAPTURE=<png path>): after a few frames,
    // save the rendered viewport and quit. This is how the windowed render is confirmed; it is not
    // part of normal runtime.
    private int _frames;
    private readonly string? _capturePath = System.Environment.GetEnvironmentVariable("FANTASIM_GLOBE_CAPTURE");

    public GlobeView(WorldGlobeSnapshot snapshot)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        Name = "GlobeView";
    }

    public override void _Ready()
    {
        _material = BuildMaterial(_snapshot);

        var globe = new MeshInstance3D
        {
            Name = "Globe",
            Mesh = BuildMesh(_snapshot),
            MaterialOverride = _material,
            Scale = Vector3.One * 2.0f,
        };
        AddChild(globe);

        var camera = new Camera3D { Name = "GlobeCamera", Current = true };
        camera.Position = new Vector3(0, 1.4f, 6.2f);
        AddChild(camera);
        camera.LookAt(Vector3.Zero, Vector3.Up); // after AddChild — LookAt needs the node in-tree

        SetTick(0);
        GD.Print($"[GlobeView] globe built: {_snapshot.CellCount} cells, {_snapshot.PlateCount} plates.");
    }

    public override void _Process(double delta)
    {
        if (string.IsNullOrEmpty(_capturePath)) return;
        if (++_frames != 15) return;
        try
        {
            var image = GetViewport()?.GetTexture()?.GetImage();
            if (image is not null)
            {
                image.SavePng(_capturePath);
                GD.Print($"[GlobeView] verification capture -> {_capturePath}");
            }
            else
            {
                GD.Print("[GlobeView] verification capture skipped (no image; headless?)");
            }
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[GlobeView] capture failed: {ex.Message}");
        }
        GetTree().Quit();
    }

    /// <summary>Set the canonical tick the globe is reconstructed at (drives the GPU rotation).</summary>
    public void SetTick(double tick)
    {
        _tick = tick;
        _material?.SetShaderParameter("u_tick", (float)tick);
    }

    public double Tick => _tick;

    private static ArrayMesh BuildMesh(WorldGlobeSnapshot s)
    {
        int triCount = s.Cells.Count;
        var verts = new Vector3[triCount * 3];
        var uvs = new Vector2[triCount * 3]; // uv.x = plate id

        for (int i = 0; i < triCount; i++)
        {
            var cell = s.Cells[i];
            int b = i * 3;
            verts[b + 0] = ToV3(cell.C0); uvs[b + 0] = new Vector2(cell.PlateId, 0f);
            verts[b + 1] = ToV3(cell.C1); uvs[b + 1] = new Vector2(cell.PlateId, 0f);
            verts[b + 2] = ToV3(cell.C2); uvs[b + 2] = new Vector2(cell.PlateId, 0f);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static ShaderMaterial BuildMaterial(WorldGlobeSnapshot s)
    {
        var mat = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };

        var axisRate = new Godot.Collections.Array();
        for (int i = 0; i < 16; i++) axisRate.Add(new Vector4(0, 0, 1, 0)); // identity default
        foreach (var plate in s.Plates)
            if (plate.PlateId >= 0 && plate.PlateId < 16)
                axisRate[plate.PlateId] = new Vector4(plate.Axis.X, plate.Axis.Y, plate.Axis.Z, (float)plate.RatePerTick);

        mat.SetShaderParameter("u_plate_axis_rate", axisRate);
        mat.SetShaderParameter("u_plate_count", s.PlateCount);
        mat.SetShaderParameter("u_tick", 0.0f);
        return mat;
    }

    private static Vector3 ToV3(GlobeVec3 v) => new Vector3(v.X, v.Y, v.Z);
}
