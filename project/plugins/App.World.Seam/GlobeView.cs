using System;
using System.Threading.Tasks;
using FantaSim.App.GpuCompute;
using FantaSim.App.World.Dto;
using Godot;
// Disambiguate: both FantaSim.App.World (the World service contract) and FantaSim.App.GpuCompute expose
// an IService; the relief render uses the GPU compute one.
using GpuComputeService = FantaSim.App.GpuCompute.IService;

namespace FantaSim.App.World.Seam;

/// <summary>
/// T4 seam: the ONLY App.World tier that touches Godot. Builds a globe <see cref="ArrayMesh"/> from a
/// <see cref="WorldGlobeSnapshot"/>, displaces it into 3D RELIEF by per-cell elevation, lights it, and
/// colours it by an elevation/biome ramp (tectonic feature-colours kept as a toggle). Each cell still
/// rotates by its plate's Euler rotation on the GPU (tick uniform — the motion spine).
///
/// <para>Sub-project C.1 (relief render): the per-cell elevation (sub-project B's
/// <c>CellElevationModel.GetElevations()</c>) is fed in via <paramref name="elevationsAt"/>. On every
/// tick change the seam dispatches a GLSL COMPUTE shader (<c>relief_displace.glsl</c>) through the
/// resident <see cref="IService">App.GpuCompute</see> service: it pushes each vertex radially out/in by
/// <c>elevation * exaggeration</c> and emits an outward flat face normal per cell. The readback updates
/// the mesh (positions + normals); the spatial shader then rotates BOTH the displaced position and the
/// normal per plate and a <see cref="Godot.DirectionalLight3D"/> sun lights the relief. If the compute
/// service is absent/unavailable, the seam falls back to a VERTEX-shader radial displacement of an
/// elevation data texture with <c>dFdx</c>/<c>dFdy</c> flat normals — still visibly lit relief.</para>
/// </summary>
public sealed partial class GlobeView : Node3D
{
    // Radius/elevation coupling: cell corners live on the unit sphere (|v|≈1); the MeshInstance is
    // scaled ×2. Elevation (sub-project B) ranges roughly −1500 (old abyssal ocean) .. +2500 (continent
    // collision), 0 at sea level. This exaggeration maps a collision (+2500) to ≈+0.3 radius and a
    // trench (−1500) to ≈−0.18 radius — relief that reads unmistakably as 3D without self-intersecting
    // the mantle shell (radius 0.965). Tunable; intentionally generous for the "visible payoff".
    private const float DisplacementExaggeration = 0.00012f;

    // Elevation normalisation for the biome ramp: divide metres by this to land most terrain in [-1,1].
    private const float ElevationColorScale = 2000.0f;

    private const string ReliefShaderPath = "res://shaders/relief_displace.glsl";

    // Spatial shader (LIT): vertex rotates each cell's displaced position AND normal about its plate's
    // Euler axis by (rate*tick); fragment colours by an elevation/biome ramp (u_color_mode 0) or the
    // per-cell tectonic feature texture (u_color_mode 1). NORMAL is written into the mesh by the compute
    // pass (or derived via dFdx/dFdy in the vertex-fallback build). No render_mode unshaded — a
    // DirectionalLight3D sun lights it via standard Lambert.
    private const string ShaderCode = @"
shader_type spatial;
render_mode cull_disabled;

uniform float u_tick = 0.0;
uniform int u_plate_count = 1;
uniform int u_color_mode = 0;                       // 0 = biome (elevation ramp), 1 = tectonic features
uniform float u_elev_scale = 2000.0;                // metres -> [-1,1]-ish for the ramp
uniform bool u_vertex_displace = false;             // fallback: displace in-shader from u_cell_elev
uniform float u_exaggeration = 0.00005;
uniform vec4 u_plate_axis_rate[16];                 // xyz = unit axis, w = radians per canonical tick
uniform sampler2D u_cell_types : filter_nearest;    // r = feature kind: 0 none,1 mtn,2 arc,3 trench,4 ridge,5 fault
uniform sampler2D u_cell_elev : filter_nearest;     // r = per-cell elevation (metres); used in the fallback path

varying vec3 v_plate_color;
varying float v_cell_u;
varying float v_elev;          // per-vertex elevation in metres (biome ramp input)
varying vec3 v_world_pos;      // for dFdx/dFdy flat normals in the fallback path

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

// Elevation -> colour ramp: deep ocean -> shallow -> beach -> lowland green -> rock -> snow.
vec3 biome_ramp(float elev_m) {
    float e = elev_m / u_elev_scale; // ~[-1,1]
    vec3 deep    = vec3(0.06, 0.18, 0.42);   // deep ocean — readable blue, not void
    vec3 shallow = vec3(0.16, 0.52, 0.72);   // shallow shelf
    vec3 beach   = vec3(0.82, 0.76, 0.52);
    vec3 low     = vec3(0.24, 0.56, 0.26);
    vec3 high    = vec3(0.46, 0.38, 0.28);
    vec3 rock    = vec3(0.56, 0.53, 0.50);
    vec3 snow    = vec3(0.96, 0.97, 0.99);
    if (e < -0.05) return mix(deep, shallow, clamp((e + 1.0) / 0.95, 0.0, 1.0));
    if (e < 0.02)  return mix(shallow, beach, clamp((e + 0.05) / 0.07, 0.0, 1.0));
    if (e < 0.15)  return mix(beach, low, clamp((e - 0.02) / 0.13, 0.0, 1.0));
    if (e < 0.45)  return mix(low, high, clamp((e - 0.15) / 0.30, 0.0, 1.0));
    if (e < 0.75)  return mix(high, rock, clamp((e - 0.45) / 0.30, 0.0, 1.0));
    return mix(rock, snow, clamp((e - 0.75) / 0.40, 0.0, 1.0));
}

void vertex() {
    int pid = int(UV.x + 0.5);
    vec4 ar = u_plate_axis_rate[pid];
    float angle = ar.w * u_tick;
    vec3 axis = normalize(ar.xyz);

    float elev = UV2.x; // metres, packed per vertex by the seam (compute path) or sampled below
    if (u_vertex_displace) {
        elev = texture(u_cell_elev, vec2(UV.y, 0.5)).r;     // fallback: per-cell elevation texture
        VERTEX += normalize(VERTEX) * (elev * u_exaggeration);
    }
    v_elev = elev;

    VERTEX = rotate_axis(VERTEX, axis, angle);
    NORMAL = rotate_axis(NORMAL, axis, angle);
    VERTEX *= (1.0 - float(pid) * 0.0008); // tiny per-plate shell offset so overlapping plate seams render clean

    float denom = max(float(u_plate_count), 1.0);
    v_plate_color = hue(float(pid) / denom) * 0.55 + 0.10;
    v_cell_u = UV.y;
    v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    // Fallback flat normal from screen-space derivatives of world position (used when the mesh carries
    // no compute normals). Robust regardless of the compute path; cheap for a faceted mesh.
    if (u_vertex_displace) {
        vec3 n = normalize(cross(dFdx(v_world_pos), dFdy(v_world_pos)));
        NORMAL = (VIEW_MATRIX * vec4(n, 0.0)).xyz; // fragment NORMAL is view-space
    }

    vec3 col;
    if (u_color_mode == 1) {
        int kind = int(texture(u_cell_types, vec2(v_cell_u, 0.5)).r + 0.5);
        col = v_plate_color;
        if (kind == 1) col = vec3(0.62, 0.43, 0.27);      // Mountain     -> brown
        else if (kind == 2) col = vec3(0.97, 0.52, 0.16); // VolcanicArc  -> orange
        else if (kind == 3) col = vec3(0.10, 0.18, 0.46); // Trench       -> deep blue
        else if (kind == 4) col = vec3(0.34, 0.82, 0.88); // Ridge        -> cyan
        else if (kind == 5) col = vec3(0.80, 0.76, 0.72); // Fault        -> light gray
    } else {
        col = biome_ramp(v_elev);
    }
    ALBEDO = col;
    ROUGHNESS = 0.95;
    SPECULAR = 0.1;
}

// Half-Lambert (Valve wrap) diffuse: softens the terminator so the displaced relief still reads as 3D
// across the whole visible disc instead of falling to black at grazing angles. Cheap and stable.
void light() {
    float ndotl = dot(normalize(NORMAL), normalize(LIGHT));
    float wrap = ndotl * 0.5 + 0.5;   // half-Lambert
    wrap *= wrap;
    DIFFUSE_LIGHT += ALBEDO * LIGHT_COLOR * ATTENUATION * wrap;
}
";

    private readonly WorldGlobeSnapshot _snapshot;
    private readonly Func<long, string> _formatTick;
    private readonly Func<long, byte[]>? _classifyAt;
    private readonly Func<long, float[]>? _elevationsAt;
    private readonly GpuComputeService? _gpuCompute;

    private ShaderMaterial? _material;
    private Image? _typeImage;
    private ImageTexture? _typeTexture;
    private Image? _elevImage;
    private ImageTexture? _elevTexture;
    private ArrayMesh? _mesh;
    private long _tick;

    // Base (undisplaced) per-vertex unit-sphere positions + the matching UV (uv.x = plateId,
    // uv.y = per-cell data-texture U). Compute reads the base positions; the readback rebuilds the
    // surface with displaced positions + normals (uv2.x carries per-vertex elevation for the ramp).
    private Vector3[] _basePos = Array.Empty<Vector3>();
    private Vector2[] _uvs = Array.Empty<Vector2>();
    private int _triCount;
    // True once a compute dispatch succeeds: the GPU path owns displacement+normals. Until then (or if
    // it fails) the seam uses the vertex-shader fallback so relief is never silently skipped.
    private bool _computePathActive;
    private bool _fallbackAnnounced;

    // Verification utility (inert unless FANTASIM_GLOBE_CAPTURE=<png path>): after a few frames,
    // save the rendered viewport and quit. Not part of normal runtime.
    private int _frames;
    private readonly string? _capturePath = System.Environment.GetEnvironmentVariable("FANTASIM_GLOBE_CAPTURE");

    private Label? _label;
    private HSlider? _slider;

    public GlobeView(
        WorldGlobeSnapshot snapshot,
        Func<long, string>? formatTick = null,
        Func<long, byte[]>? classifyAt = null,
        Func<long, float[]>? elevationsAt = null,
        GpuComputeService? gpuCompute = null)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _formatTick = formatTick ?? (t => t.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _classifyAt = classifyAt;
        _elevationsAt = elevationsAt;
        _gpuCompute = gpuCompute;
        Name = "GlobeView";
    }

    public override void _Ready()
    {
        _material = BuildMaterial(_snapshot);
        InitCellTypeTexture(_snapshot.CellCount);
        InitCellElevTexture(_snapshot.CellCount);

        AddChild(BuildMantle()); // base sphere under the plate shell: divergent gaps reveal mantle

        _mesh = BuildMesh(_snapshot, out _basePos, out _uvs);
        _triCount = _snapshot.Cells.Count;

        var globe = new MeshInstance3D
        {
            Name = "Globe",
            Mesh = _mesh,
            MaterialOverride = _material,
            Scale = Vector3.One * 2.0f,
        };
        AddChild(globe);

        // The sun: a directional light so the displaced relief reads as 3D (Lambert from the per-cell
        // face normals the compute pass writes / the fallback derives). Replaces the old unshaded look.
        // Aimed from front-upper-right TOWARD the globe so the camera-facing hemisphere (the +Z face the
        // camera looks at) is lit and the terminator does not eat the visible disc; the grazing angle
        // throws relief shadows that make mountains/trenches read as 3D.
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            LightEnergy = 1.6f,
            ShadowEnabled = false,
        };
        AddChild(sun);
        // Near-frontal (close to the camera axis at +Z, slight up/right offset) so almost the whole
        // visible hemisphere is lit while the offset still rakes relief shadows across mountains/valleys.
        // A more grazing sun pushed the terminator through the disc centre and lost half the payoff.
        sun.Position = new Vector3(1.1f, 1.9f, 7.5f);
        sun.LookAt(Vector3.Zero, Vector3.Up);

        // A little ambient/sky fill so shadowed (night-side) relief is not pure black.
        var env = new WorldEnvironment { Name = "GlobeEnv", Environment = BuildEnvironment() };
        AddChild(env);

        var camera = new Camera3D { Name = "GlobeCamera", Current = true };
        camera.Position = new Vector3(0, 1.4f, 6.2f);
        AddChild(camera);
        camera.LookAt(Vector3.Zero, Vector3.Up); // after AddChild — LookAt needs the node in-tree

        BuildScrubber();
        // Colour view: default biome (0); FANTASIM_GLOBE_COLORMODE=1 selects the tectonic feature view.
        SetColorMode((int)ParseEnvLong("FANTASIM_GLOBE_COLORMODE", 0));
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

    /// <summary>Set the canonical tick: re-derives per-cell elevation (B), recomputes the relief
    /// displacement (GPU compute, or the vertex fallback), updates the mesh + feature texture + ladder
    /// label + scrubber, and drives the GPU rotation uniform (the motion spine).</summary>
    public void SetTick(long tick)
    {
        _tick = tick;
        _material?.SetShaderParameter("u_tick", (float)tick);
        UpdateCellTypes(tick);
        UpdateRelief(tick);
        if (_label is not null) _label.Text = _formatTick(tick);
        if (_slider is not null && (long)_slider.Value != tick) _slider.Value = tick;
    }

    public long Tick => _tick;

    /// <summary>Colour view: 0 = elevation/biome ramp (default), 1 = tectonic feature colours.</summary>
    public void SetColorMode(int mode) => _material?.SetShaderParameter("u_color_mode", mode == 1 ? 1 : 0);

    /// <summary>True once a GPU-compute displacement has succeeded (else the vertex-shader fallback is live).</summary>
    public bool ComputePathActive => _computePathActive;

    // --- Relief: per-cell elevation -> displaced positions + normals -------------------------------

    private void UpdateRelief(long tick)
    {
        if (_elevationsAt is null || _mesh is null) return;

        float[] elevByCell;
        try { elevByCell = _elevationsAt(tick); }
        catch (Exception ex) { GD.PushWarning($"[GlobeView] elevation fetch failed: {ex.Message}"); return; }

        // Always refresh the per-cell elevation texture (fallback displacement source + a stable record).
        UpdateCellElevTexture(elevByCell);

        // Expand per-cell elevation to per-vertex (3 verts/cell), for the biome ramp + the compute input.
        int vertCount = _triCount * 3;
        var elevPerVert = new float[vertCount];
        for (int i = 0; i < _triCount; i++)
        {
            int cellId = _snapshot.Cells[i].CellId;
            float e = (cellId >= 0 && cellId < elevByCell.Length) ? elevByCell[cellId] : 0f;
            int b = i * 3;
            elevPerVert[b + 0] = e;
            elevPerVert[b + 1] = e;
            elevPerVert[b + 2] = e;
        }

        // Capture-mode diagnostic: report the relief extent so the displacement exaggeration can be
        // calibrated against the actual elevation range at the captured tick.
        if (!string.IsNullOrEmpty(_capturePath))
        {
            float emin = float.MaxValue, emax = float.MinValue;
            foreach (var e in elevByCell) { if (e < emin) emin = e; if (e > emax) emax = e; }
            GD.Print($"[GlobeView] relief extent @tick {tick}: elevation [{emin:F1}, {emax:F1}] m -> radial displace " +
                     $"[{emin * DisplacementExaggeration:F4}, {emax * DisplacementExaggeration:F4}] (×2 scale -> world ×2).");
        }

        if (TryComputeRelief(elevPerVert, out var displaced, out var normals))
        {
            RebuildSurface(displaced, normals, elevPerVert);
            if (!_computePathActive)
            {
                _computePathActive = true;
                _material?.SetShaderParameter("u_vertex_displace", false);
                GD.Print("[GlobeView] relief: GPU compute path active (displaced positions + face normals).");
            }
        }
        else
        {
            // Fallback: the vertex shader displaces from u_cell_elev and derives flat normals via dFdx/dFdy.
            // Keep the mesh at base positions but carry per-vertex elevation in uv2 for the ramp.
            RebuildSurfaceBaseWithElev(elevPerVert);
            _material?.SetShaderParameter("u_vertex_displace", true);
            if (!_fallbackAnnounced)
            {
                _fallbackAnnounced = true;
                GD.Print("[GlobeView] relief: GPU compute unavailable — using vertex-shader displacement fallback (dFdx/dFdy normals).");
            }
        }
    }

    // Dispatch relief_displace.glsl over the base positions + per-vertex elevation; read back displaced
    // positions and normals. Returns false (caller falls back) if no service, no device, or any failure.
    private bool TryComputeRelief(float[] elevPerVert, out Vector3[] displaced, out Vector3[] normals)
    {
        displaced = Array.Empty<Vector3>();
        normals = Array.Empty<Vector3>();
        if (_gpuCompute is null) return false;
        if (!_gpuCompute.Capabilities.IsAvailable) return false;

        int vertCount = _triCount * 3;

        // base positions -> vec4 (xyz, 0) std430
        var baseData = new byte[vertCount * 16];
        for (int v = 0; v < vertCount; v++)
            WriteVec4(baseData, v * 16, _basePos[v].X, _basePos[v].Y, _basePos[v].Z, 0f);

        // per-vertex elevation -> float std430
        var elevData = new byte[vertCount * 4];
        Buffer.BlockCopy(elevPerVert, 0, elevData, 0, elevData.Length);

        // output buffers (zero-init); read back fully
        var outPosData = new byte[vertCount * 16];
        var outNrmData = new byte[vertCount * 16];

        // params: uint triCount, float exaggeration
        var paramData = new byte[8];
        Buffer.BlockCopy(new uint[] { (uint)_triCount }, 0, paramData, 0, 4);
        Buffer.BlockCopy(new float[] { DisplacementExaggeration }, 0, paramData, 4, 4);

        var request = new ComputeDispatchRequest(
            new ComputeShaderReference("world.relief.displace", ReliefShaderPath),
            new ComputeDispatchSize((uint)((_triCount + 63) / 64), 1, 1), // local_size_x = 64 over triangles
            new[]
            {
                new ComputeBufferBinding(0, 0, baseData),
                new ComputeBufferBinding(0, 1, elevData),
                new ComputeBufferBinding(0, 2, outPosData, (uint)outPosData.Length, "pos"),
                new ComputeBufferBinding(0, 3, outNrmData, (uint)outNrmData.Length, "nrm"),
                new ComputeBufferBinding(0, 4, paramData),
            });

        try
        {
            // The compute service is async but the resident backend runs Submit()+Sync() synchronously
            // on the calling thread; this is invoked from the main thread on a tick change, so blocking
            // for the (sub-millisecond) readback keeps the mesh update atomic with the tick.
            var result = _gpuCompute.DispatchAsync(request).GetAwaiter().GetResult();
            if (!result.Succeeded ||
                !result.ReadbackBuffers.TryGetValue("pos", out var posBytes) ||
                !result.ReadbackBuffers.TryGetValue("nrm", out var nrmBytes))
            {
                if (!_computePathActive && !string.IsNullOrEmpty(result.ErrorMessage))
                    GD.PushWarning($"[GlobeView] relief compute dispatch failed: {result.ErrorMessage}");
                return false;
            }

            displaced = ReadVec3FromVec4(posBytes, vertCount);
            normals = ReadVec3FromVec4(nrmBytes, vertCount);
            return true;
        }
        catch (Exception ex)
        {
            if (!_computePathActive) GD.PushWarning($"[GlobeView] relief compute threw: {ex.Message}");
            return false;
        }
    }

    private void RebuildSurface(Vector3[] verts, Vector3[] normals, float[] elevPerVert)
    {
        var uv2 = new Vector2[verts.Length];
        for (int i = 0; i < verts.Length; i++) uv2[i] = new Vector2(elevPerVert[i], 0f);

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = _uvs;
        arrays[(int)Mesh.ArrayType.TexUV2] = uv2;
        ReplaceSurface(arrays);
    }

    // Fallback surface: keep BASE positions (the vertex shader does the radial displacement), carry the
    // per-vertex elevation in uv2 for the ramp, and let the fragment derive normals via dFdx/dFdy.
    private void RebuildSurfaceBaseWithElev(float[] elevPerVert)
    {
        var uv2 = new Vector2[_basePos.Length];
        for (int i = 0; i < _basePos.Length; i++) uv2[i] = new Vector2(elevPerVert[i], 0f);

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _basePos;
        arrays[(int)Mesh.ArrayType.TexUV] = _uvs;
        arrays[(int)Mesh.ArrayType.TexUV2] = uv2;
        ReplaceSurface(arrays);
    }

    private void ReplaceSurface(Godot.Collections.Array arrays)
    {
        if (_mesh is null) return;
        if (_mesh.GetSurfaceCount() > 0) _mesh.ClearSurfaces();
        _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    }

    private void InitCellTypeTexture(int cellCount)
    {
        _typeImage = Image.CreateEmpty(Math.Max(cellCount, 1), 1, false, Image.Format.Rf);
        _typeTexture = ImageTexture.CreateFromImage(_typeImage);
        _material?.SetShaderParameter("u_cell_types", _typeTexture);
    }

    private void InitCellElevTexture(int cellCount)
    {
        _elevImage = Image.CreateEmpty(Math.Max(cellCount, 1), 1, false, Image.Format.Rf);
        _elevTexture = ImageTexture.CreateFromImage(_elevImage);
        _material?.SetShaderParameter("u_cell_elev", _elevTexture);
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

    private void UpdateCellElevTexture(float[] elevByCell)
    {
        if (_elevImage is null || _elevTexture is null) return;
        int count = Math.Min(elevByCell.Length, _elevImage.GetWidth());
        for (int c = 0; c < count; c++)
            _elevImage.SetPixel(c, 0, new Color(elevByCell[c], 0f, 0f));
        _elevTexture.Update(_elevImage);
    }

    // --- Time scrubber (unchanged) -----------------------------------------------------------------

    private void BuildScrubber()
    {
        var layer = new CanvasLayer { Name = "ScrubberLayer" };
        AddChild(layer);

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

        _slider = new HSlider
        {
            MinValue = 0,
            MaxValue = 100.0 * _snapshot.TicksPerAnchor,
            Step = _snapshot.TicksPerAnchor / 2.0,
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

    // Build the base mesh: 3 verts/cell. Also returns the base positions + uvs so the relief compute
    // pass can read them and the per-tick surface rebuild can reuse the uvs.
    private static ArrayMesh BuildMesh(WorldGlobeSnapshot s, out Vector3[] basePos, out Vector2[] uvs)
    {
        int triCount = s.Cells.Count;
        var verts = new Vector3[triCount * 3];
        var uv = new Vector2[triCount * 3]; // uv.x = plate id, uv.y = per-cell data-texture U coord

        float cellCount = Math.Max(s.CellCount, 1);
        for (int i = 0; i < triCount; i++)
        {
            var cell = s.Cells[i];
            float u = (cell.CellId + 0.5f) / cellCount;
            int b = i * 3;
            verts[b + 0] = ToV3(cell.C0); uv[b + 0] = new Vector2(cell.PlateId, u);
            verts[b + 1] = ToV3(cell.C1); uv[b + 1] = new Vector2(cell.PlateId, u);
            verts[b + 2] = ToV3(cell.C2); uv[b + 2] = new Vector2(cell.PlateId, u);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.TexUV] = uv;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        basePos = verts;
        uvs = uv;
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
        mat.SetShaderParameter("u_color_mode", 0);           // default: elevation/biome ramp
        mat.SetShaderParameter("u_elev_scale", ElevationColorScale);
        mat.SetShaderParameter("u_exaggeration", DisplacementExaggeration);
        mat.SetShaderParameter("u_vertex_displace", false);
        return mat;
    }

    private static Godot.Environment BuildEnvironment()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.02f, 0.02f, 0.04f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.45f, 0.50f, 0.60f),
            AmbientLightEnergy = 0.85f,
        };
        return env;
    }

    // The inner mantle shell: must sit BELOW the deepest radial ocean displacement (base radius 1.0 −
    // |elev|·exaggeration can reach ≈0.82 at the widest elevation range) or it occludes the sunken ocean
    // floor and reads as a dark void. Radius 0.78 keeps it hidden under the relief except true plate
    // gaps; lit (not unshaded) so it tones with the sun instead of punching black.
    private static MeshInstance3D BuildMantle() => new()
    {
        Name = "Mantle",
        Mesh = new SphereMesh { Radius = 0.78f, Height = 1.56f, RadialSegments = 48, Rings = 24 },
        Scale = Vector3.One * 2.0f,
        MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.10f, 0.14f, 0.26f),
            Roughness = 1.0f,
        },
    };

    private static void WriteVec4(byte[] dst, int offset, float x, float y, float z, float w)
    {
        BitConverter.TryWriteBytes(dst.AsSpan(offset + 0, 4), x);
        BitConverter.TryWriteBytes(dst.AsSpan(offset + 4, 4), y);
        BitConverter.TryWriteBytes(dst.AsSpan(offset + 8, 4), z);
        BitConverter.TryWriteBytes(dst.AsSpan(offset + 12, 4), w);
    }

    private static Vector3[] ReadVec3FromVec4(byte[] src, int count)
    {
        var result = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            int o = i * 16;
            result[i] = new Vector3(
                BitConverter.ToSingle(src, o + 0),
                BitConverter.ToSingle(src, o + 4),
                BitConverter.ToSingle(src, o + 8));
        }
        return result;
    }

    private static Vector3 ToV3(GlobeVec3 v) => new Vector3(v.X, v.Y, v.Z);
}
