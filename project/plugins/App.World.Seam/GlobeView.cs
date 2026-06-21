using System;
using FantaSim.App.World.Dto;
using Godot;

namespace FantaSim.App.World.Seam;

/// <summary>
/// T4 seam: the ONLY App.World tier that touches Godot. Builds a globe <see cref="ArrayMesh"/> from a
/// <see cref="WorldGlobeSnapshot"/> and rotates each cell by its plate's Euler rotation on the GPU
/// (tick uniform). Boundary cells are coloured by their type (convergent/divergent/transform),
/// re-derived per tick on the CPU and pushed through a per-cell data texture the shader samples.
/// </summary>
public sealed partial class GlobeView : Node3D
{
    // Spatial shader: vertex rotates each cell about its plate's Euler axis by (rate*tick) and dims
    // the plate hue; fragment overlays the per-cell boundary-type colour sampled from u_cell_types.
    private const string ShaderCode = @"
shader_type spatial;
render_mode unshaded, cull_disabled;

uniform float u_tick = 0.0;
uniform int u_plate_count = 1;
uniform vec4 u_plate_axis_rate[16];                 // xyz = unit axis, w = radians per canonical tick
uniform sampler2D u_cell_types : filter_nearest;    // r = boundary type: 0 interior,1 conv,2 div,3 transform

varying vec3 v_plate_color;
varying float v_cell_u;

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
    VERTEX *= (1.0 - float(pid) * 0.004); // per-plate shell offset: convergent overlaps render clean
    float denom = max(float(u_plate_count), 1.0);
    v_plate_color = hue(float(pid) / denom) * 0.55 + 0.10; // dimmed so boundary colours pop
    v_cell_u = UV.y;
}

void fragment() {
    int kind = int(texture(u_cell_types, vec2(v_cell_u, 0.5)).r + 0.5); // 0 none,1 mtn,2 arc,3 trench,4 ridge,5 fault
    vec3 col = v_plate_color;
    if (kind == 1) col = vec3(0.62, 0.43, 0.27);      // Mountain     -> brown
    else if (kind == 2) col = vec3(0.97, 0.52, 0.16); // VolcanicArc  -> orange
    else if (kind == 3) col = vec3(0.10, 0.18, 0.46); // Trench       -> deep blue
    else if (kind == 4) col = vec3(0.34, 0.82, 0.88); // Ridge        -> cyan
    else if (kind == 5) col = vec3(0.80, 0.76, 0.72); // Fault        -> light gray
    ALBEDO = col;
}
";

    private readonly WorldGlobeSnapshot _snapshot;
    private readonly Func<long, string> _formatTick;
    private readonly Func<long, byte[]>? _classifyAt;
    private ShaderMaterial? _material;
    private Image? _typeImage;
    private ImageTexture? _typeTexture;
    private long _tick;

    // Verification utility (inert unless FANTASIM_GLOBE_CAPTURE=<png path>): after a few frames,
    // save the rendered viewport and quit. Not part of normal runtime.
    private int _frames;
    private readonly string? _capturePath = System.Environment.GetEnvironmentVariable("FANTASIM_GLOBE_CAPTURE");

    private Label? _label;
    private HSlider? _slider;

    public GlobeView(
        WorldGlobeSnapshot snapshot,
        Func<long, string>? formatTick = null,
        Func<long, byte[]>? classifyAt = null)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _formatTick = formatTick ?? (t => t.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _classifyAt = classifyAt;
        Name = "GlobeView";
    }

    public override void _Ready()
    {
        _material = BuildMaterial(_snapshot);
        InitCellTypeTexture(_snapshot.CellCount);

        AddChild(BuildMantle()); // base sphere under the plate shell: divergent gaps reveal mantle

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

        BuildScrubber();
        long initialTick = ParseEnvLong("FANTASIM_GLOBE_TICK", 0);
        SetTick(initialTick);
        GD.Print($"[GlobeView] globe built: {_snapshot.CellCount} cells, {_snapshot.PlateCount} plates, t0={_formatTick(initialTick)}.");
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

    /// <summary>Set the canonical tick: drives the GPU rotation, the ladder label, and re-derives the
    /// per-cell boundary classification (boundary colours) for that tick.</summary>
    public void SetTick(long tick)
    {
        _tick = tick;
        _material?.SetShaderParameter("u_tick", (float)tick);
        UpdateCellTypes(tick);
        if (_label is not null) _label.Text = _formatTick(tick);
        if (_slider is not null && (long)_slider.Value != tick) _slider.Value = tick;
    }

    public long Tick => _tick;

    private void InitCellTypeTexture(int cellCount)
    {
        _typeImage = Image.CreateEmpty(Math.Max(cellCount, 1), 1, false, Image.Format.Rf);
        _typeTexture = ImageTexture.CreateFromImage(_typeImage);
        _material?.SetShaderParameter("u_cell_types", _typeTexture);
    }

    private void UpdateCellTypes(long tick)
    {
        if (_classifyAt is null || _typeImage is null || _typeTexture is null) return;
        var types = _classifyAt(tick);
        int count = Math.Min(types.Length, _typeImage.GetWidth());
        for (int c = 0; c < count; c++)
            _typeImage.SetPixel(c, 0, new Color(types[c], 0f, 0f));
        _typeTexture.Update(_typeImage);
    }

    // --- Time scrubber: an HSlider over canonical TICKS (0..100 ka) drives SetTick; the label is
    //     rendered through the OdometerLadder (CanonicalTimeLabel), never real-world Ma. ---

    private void BuildScrubber()
    {
        var layer = new CanvasLayer { Name = "ScrubberLayer" };
        AddChild(layer);

        // A fixed-height bar pinned to the bottom edge (explicit anchors + offsets so it always lays out).
        var panel = new PanelContainer();
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
        panel.OffsetTop = -60;
        layer.AddChild(panel);

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 16);
        panel.AddChild(hbox);

        _label = new Label
        {
            Text = _formatTick(0),
            CustomMinimumSize = new Vector2(170, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _label.AddThemeFontSizeOverride("font_size", 26);
        hbox.AddChild(_label);

        // 0 .. 100 ka of canonical time (1 ka = TicksPerMegaAnnum ticks), stepped at half a ka.
        _slider = new HSlider
        {
            MinValue = 0,
            MaxValue = 100.0 * _snapshot.TicksPerMegaAnnum,
            Step = _snapshot.TicksPerMegaAnnum / 2.0,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        _slider.ValueChanged += OnScrubberChanged;
        hbox.AddChild(_slider);
    }

    private void OnScrubberChanged(double tick) => SetTick((long)tick);

    private static long ParseEnvLong(string name, long fallback)
    {
        var raw = System.Environment.GetEnvironmentVariable(name);
        return long.TryParse(raw, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    private static ArrayMesh BuildMesh(WorldGlobeSnapshot s)
    {
        int triCount = s.Cells.Count;
        var verts = new Vector3[triCount * 3];
        var uvs = new Vector2[triCount * 3]; // uv.x = plate id, uv.y = per-cell data-texture U coord

        float cellCount = Math.Max(s.CellCount, 1);
        for (int i = 0; i < triCount; i++)
        {
            var cell = s.Cells[i];
            float u = (cell.CellId + 0.5f) / cellCount;
            int b = i * 3;
            verts[b + 0] = ToV3(cell.C0); uvs[b + 0] = new Vector2(cell.PlateId, u);
            verts[b + 1] = ToV3(cell.C1); uvs[b + 1] = new Vector2(cell.PlateId, u);
            verts[b + 2] = ToV3(cell.C2); uvs[b + 2] = new Vector2(cell.PlateId, u);
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

    private static MeshInstance3D BuildMantle() => new()
    {
        Name = "Mantle",
        Mesh = new SphereMesh { Radius = 0.965f, Height = 1.93f, RadialSegments = 48, Rings = 24 },
        Scale = Vector3.One * 2.0f,
        MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.16f, 0.15f, 0.19f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        },
    };

    private static Vector3 ToV3(GlobeVec3 v) => new Vector3(v.X, v.Y, v.Z);
}
