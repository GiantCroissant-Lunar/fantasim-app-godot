using System;
using System.Collections.Generic;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using FantaSim.Cartography.Globe;
using FantaSim.Cartography.Shared;
using Godot;

namespace FantaSim.App.World.Seam;

/// <summary>
/// T4 seam: the ONLY App.World tier that touches Godot. Turns the T3 <see cref="GlobePlateSurfaces"/>'
/// WATERTIGHT per-plate caps into Godot <see cref="ArrayMesh"/>es — one <see cref="MeshInstance3D"/> per
/// plate — lights them, colours them by an elevation/biome ramp (tectonic feature-colours as a toggle),
/// and rotates each cap rigidly by its plate's Euler pole × tick on the GPU (the motion spine).
///
/// <para><b>Un-shattering (this is the fix).</b> The old globe drew one triangle per cell with three
/// UNSHARED corners, each pushed out by that cell's own elevation; neighbours at different heights left
/// gaps and the ball cracked. Now the geometry arrives pre-built and watertight from cartography
/// (<see cref="GlobeSurface"/>): within a plate, corners are SHARED and each shared corner sits at ONE
/// height (the mean of the cells touching it). For a BLOCKY (flat-shaded) look the seam still triples a
/// triangle's vertices and assigns the triangle's single <see cref="GlobeSurface.FlatNormals"/> to all
/// three — but the POSITIONS come from the watertight shared-vertex <see cref="GlobeSurface.Positions"/>,
/// so adjacent triangles meet EXACTLY at shared corners: faceted yet crack-free.</para>
///
/// <para><b>Pre-displaced, no GPU compute.</b> Because cartography already displaced the surface and
/// supplied flat normals, the seam no longer dispatches the relief GPU-compute shader nor the
/// elevation-texture vertex fallback — that path is removed (the App.GpuCompute plugin stays in the app
/// for LOD later). The shader keeps: per-plate rotate(pos+normal), the biome/tectonic colouring, the
/// half-Lambert <c>light()</c>, the sun, the mantle, and the scrubber. Boundaries BETWEEN plates may
/// gap/overlap as plates drift — expected for motion-model A; ridge/trench boundary fill is a later task.</para>
/// </summary>
public sealed partial class GlobeView : Node3D
{
    // Radius/elevation coupling (unchanged from the loose-tile relief): cap corners live on the unit
    // sphere (|v|≈1) displaced to 1 + elevation*exaggeration by cartography; the MeshInstance is scaled
    // ×2. Crust is the focused layer — this exaggeration is intentionally generous for the visible payoff.
    private const float DisplacementExaggeration = 0.00012f;

    // Elevation normalisation for the biome ramp: divide metres by this to land most terrain in [-1,1].
    private const float ElevationColorScale = 2000.0f;

    // Spatial shader (LIT): vertex rotates each cap's (already-displaced) position AND its flat normal
    // about its plate's Euler axis by (rate*tick); fragment colours by an elevation/biome ramp
    // (u_color_mode 0) or the per-cell tectonic feature texture (u_color_mode 1). NORMAL arrives in the
    // mesh (the flat per-triangle normal cartography supplied). A DirectionalLight3D sun lights it via
    // the half-Lambert light() below — no render_mode unshaded.
    private const string ShaderCode = @"
shader_type spatial;
render_mode cull_disabled;

uniform float u_tick = 0.0;
uniform int u_plate_count = 1;
uniform int u_color_mode = 0;                       // 0 = biome (elevation ramp), 1 = tectonic features
uniform float u_elev_scale = 2000.0;                // metres -> [-1,1]-ish for the ramp
uniform vec4 u_plate_axis_rate[16];                 // xyz = unit axis, w = radians per canonical tick
uniform sampler2D u_cell_types : filter_nearest;    // r = feature kind: 0 none,1 mtn,2 arc,3 trench,4 ridge,5 fault

varying vec3 v_plate_color;
varying float v_cell_u;
varying float v_elev;          // per-vertex elevation in metres (biome ramp input), packed in UV2.x

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

    v_elev = UV2.x; // metres, packed per vertex by the seam (per-cell elevation for the ramp)

    VERTEX = rotate_axis(VERTEX, axis, angle);
    NORMAL = rotate_axis(NORMAL, axis, angle);
    VERTEX *= (1.0 - float(pid) * 0.0008); // tiny per-plate shell offset so overlapping plate seams render clean

    float denom = max(float(u_plate_count), 1.0);
    v_plate_color = hue(float(pid) / denom) * 0.55 + 0.10;
    v_cell_u = UV.y;
}

void fragment() {
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
    private readonly GlobePlateSurfaces _surfaces;
    private readonly Func<long, string> _formatTick;
    private readonly Func<long, byte[]>? _classifyAt;
    private readonly Func<long, double[]>? _elevationsAt;
    private readonly Func<long, double?>? _magmaSurfaceTemperatureKAt;

    private ShaderMaterial? _material;
    private StandardMaterial3D? _mantleMaterial;
    private Image? _typeImage;
    private ImageTexture? _typeTexture;
    private long _tick;

    // Set permanently to true the first time the elevation source throws ObjectDisposedException
    // (i.e. the ECS world was disposed during shutdown). Once set, UpdateCaps skips the fetch
    // silently — no further PushWarning spam for a condition that is expected at teardown.
    private bool _elevationSourceUnavailable;
    private bool _thermalSourceUnavailable;

    // Regime state — set by RegimeTimelineTransport (or SetTick callers) via SetRegime.
    // When _showsPlateFeatures = false (magma-ocean / stagnant-lid), cap geometry is hidden and
    // the colour-mode shows the regime's field (surface-temperature or heat-flow).
    private string _currentRegimeId = "mobile-plate";
    private bool _showsPlateFeatures = true;
    private bool _capVisibilityDirty;

    // Per-plate cap nodes, rebuilt (positions/normals) per tick from the watertight GlobeSurfaces. The
    // shared per-cell data-texture U (uv.y) is identical to the old loose-tile UV: (cellId+0.5)/cellCount.
    private Node3D? _capRoot;
    private readonly List<MeshInstance3D> _capInstances = new();

    // Verification utility (inert unless FANTASIM_GLOBE_CAPTURE=<png path>): after a few frames,
    // save the rendered viewport and quit. Not part of normal runtime.
    private int _frames;
    private readonly string? _capturePath = System.Environment.GetEnvironmentVariable("FANTASIM_GLOBE_CAPTURE");

    private long _maxTick;

    public GlobeView(
        WorldGlobeSnapshot snapshot,
        GlobePlateSurfaces surfaces,
        Func<long, string>? formatTick = null,
        Func<long, byte[]>? classifyAt = null,
        Func<long, double[]>? elevationsAt = null,
        Func<long, double?>? magmaSurfaceTemperatureKAt = null)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _surfaces = surfaces ?? throw new ArgumentNullException(nameof(surfaces));
        _formatTick = formatTick ?? (t => t.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _classifyAt = classifyAt;
        _elevationsAt = elevationsAt;
        _magmaSurfaceTemperatureKAt = magmaSurfaceTemperatureKAt;
        _maxTick = 100L * snapshot.TicksPerAnchor; // conservative default; Host calls SetMaxTick with the real range
        Name = "GlobeView";
    }

    public override void _Ready()
    {
        _material = BuildMaterial(_snapshot);
        InitCellTypeTexture(_snapshot.CellCount);

        _mantleMaterial = BuildMantleMaterial();
        AddChild(BuildMantle(_mantleMaterial)); // base sphere under the plate shell: divergent gaps reveal mantle

        // Root holding one MeshInstance3D per plate cap; scaled ×2 like the old single globe instance.
        _capRoot = new Node3D { Name = "Globe", Scale = Vector3.One * 2.0f };
        AddChild(_capRoot);

        // The sun: a directional light so the displaced relief reads as 3D (Lambert from the per-cell
        // flat face normals cartography supplied). Aimed from front-upper-right TOWARD the globe so the
        // camera-facing hemisphere is lit and the terminator does not eat the visible disc; the grazing
        // angle throws relief shadows that make mountains/trenches read as 3D.
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            LightEnergy = 1.6f,
            ShadowEnabled = false,
        };
        AddChild(sun);
        sun.Position = new Vector3(1.1f, 1.9f, 7.5f);
        sun.LookAt(Vector3.Zero, Vector3.Up);

        // A little ambient/sky fill so shadowed (night-side) relief is not pure black.
        var env = new WorldEnvironment { Name = "GlobeEnv", Environment = BuildEnvironment() };
        AddChild(env);

        var camera = new Camera3D { Name = "GlobeCamera", Current = true };
        camera.Position = new Vector3(0, 1.4f, 6.2f);
        AddChild(camera);
        camera.LookAt(Vector3.Zero, Vector3.Up); // after AddChild — LookAt needs the node in-tree

        // Colour view: default biome (0); FANTASIM_GLOBE_COLORMODE=1 selects the tectonic feature view.
        SetColorMode((int)ParseEnvLong("FANTASIM_GLOBE_COLORMODE", 0));
        long initialTick = ParseEnvLong("FANTASIM_GLOBE_TICK", 0);
        SetTick(initialTick);
        GD.Print($"[GlobeView] globe built: {_snapshot.CellCount} cells, {_snapshot.PlateCount} plates, " +
                 $"{_capInstances.Count} watertight caps, t0={_formatTick(initialTick)}.");
    }

    public override void _Process(double delta)
    {
        // Flush pending cap-visibility refresh (e.g. SetRegime changed _showsPlateFeatures).
        if (_capVisibilityDirty)
        {
            _capVisibilityDirty = false;
            ApplyCapVisibility();
        }

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

    // Show or hide all plate-cap instances based on the current regime's ShowsPlateFeatures flag.
    // When _showsPlateFeatures = false (magma-ocean/stagnant-lid), caps are invisible — only the
    // mantle sphere shows, giving the pre-plate globe the right look (molten / cool lid).
    private void ApplyCapVisibility()
    {
        foreach (var inst in _capInstances)
            inst.Visible = _showsPlateFeatures;
    }

    /// <summary>Set the canonical tick: re-derives per-cell elevation (B), rebuilds the WATERTIGHT cap
    /// meshes (cached topology, fresh heights, via cartography), updates the feature texture + ladder
    /// label + scrubber, and drives the GPU rotation uniform (the motion spine).</summary>
    public void SetTick(long tick)
    {
        _tick = tick;
        _material?.SetShaderParameter("u_tick", (float)tick);
        UpdateCellTypes(tick);
        UpdateCaps(tick);
        UpdateRegimeThermalTint(tick);
    }

    public long Tick => _tick;

    /// <summary>
    /// Set the scrubber's maximum reachable tick. Must be called BEFORE <see cref="_Ready"/> fires
    /// (i.e. before the node is added to the scene tree) so <see cref="BuildScrubber"/> picks it up.
    /// Called by <c>Host.ComposeWorldView</c> with the same <c>maxTransportTick</c> the transport uses,
    /// ensuring the slider covers the full playback range.
    /// </summary>
    public void SetMaxTick(long maxTick)
    {
        _maxTick = maxTick > 0 ? maxTick : throw new ArgumentOutOfRangeException(nameof(maxTick));
    }

    /// <summary>Colour view: 0 = elevation/biome ramp (default), 1 = tectonic feature colours.</summary>
    public void SetColorMode(int mode) => _material?.SetShaderParameter("u_color_mode", mode == 1 ? 1 : 0);

    /// <summary>
    /// Regime threading: called by <see cref="RegimeTimelineTransport"/> (or any scrubber caller)
    /// each tick so the globe colour and cap visibility track the current regime.
    /// <list type="bullet">
    ///   <item><paramref name="regimeId"/> — e.g. "magma-ocean", "stagnant-lid", "mobile-plate".</item>
    ///   <item><paramref name="showsPlateFeatures"/> — false in pre-plate regimes; hides cap mesh instances.</item>
    ///   <item><paramref name="colorByField"/> — optional override: "surface-temperature-k" (magma-ocean),
    ///     "heat-flow-mw-m2" (stagnant-lid); null = keep current (mobile-plate uses plate/biome coloring).</item>
    /// </list>
    /// When <paramref name="showsPlateFeatures"/> changes, cap visibility is refreshed immediately.
    /// </summary>
    public void SetRegime(string regimeId, bool showsPlateFeatures, string? colorByField)
    {
        _currentRegimeId = regimeId;

        bool visibilityChanged = showsPlateFeatures != _showsPlateFeatures;
        _showsPlateFeatures = showsPlateFeatures;

        // Apply colour-by-field override: map well-known field ids to shader colour modes.
        // "surface-temperature-k" and "heat-flow-mw-m2" both use the elevation ramp for now
        // (mode 0 = biome ramp); in a future task a dedicated thermal/flux ramp can be added.
        // Mobile-plate (null) leaves the user's current color-mode choice intact.
        if (colorByField is not null)
        {
            // Pre-plate regimes: always use biome ramp (mode 0) — the elevation field is flat
            // (no tectonic features yet), giving a uniform-tinted globe that reads as "molten"
            // or "cool lid". A dedicated thermal shader colour can be added in Plan 5.
            SetColorMode(0);
        }

        if (visibilityChanged)
            QueueCapVisibilityRefresh();
        UpdateRegimeThermalTint(_tick);
    }

    /// <summary>Schedules a cap-visibility refresh on the next available frame (deferred so it
    /// runs after cap instances are built, even when called from <see cref="_Ready"/>).</summary>
    public void QueueCapVisibilityRefresh()
    {
        _capVisibilityDirty = true;
    }

    /// <summary>
    /// The current regime id (set by the transport each tick). Read-only from outside;
    /// <see cref="SetRegime"/> is the write path.
    /// </summary>
    public string CurrentRegimeId => _currentRegimeId;

    /// <summary>Number of plate caps currently mounted (one watertight cap per non-empty plate).</summary>
    public int CapCount => _capInstances.Count;

    // --- Watertight per-plate caps: build blocky (flat-shaded) ArrayMeshes from the GlobeSurfaces -----

    private void UpdateCaps(long tick)
    {
        if (_capRoot is null) return;

        double[] elevByCell;
        if (_elevationsAt is not null && !_elevationSourceUnavailable)
        {
            try { elevByCell = _elevationsAt(tick); }
            catch (ObjectDisposedException)
            {
                // The ECS world (ArchSystemRunner) was disposed before Godot finished tearing down
                // this node — expected during CoordinatedShutdown. Suppress all further fetches and
                // warnings for this condition; the globe is being destroyed anyway.
                _elevationSourceUnavailable = true;
                return;
            }
            catch (Exception ex)
            {
                GD.PushWarning($"[GlobeView] elevation fetch failed: {ex.Message}");
                elevByCell = Array.Empty<double>();
            }
        }
        else
        {
            elevByCell = Array.Empty<double>();
        }

        // Always refresh the per-cell elevation texture record for the (now flat-shaded) caps' UV map.
        var caps = _surfaces.BuildSurfaces(elevByCell, DisplacementExaggeration);

        // Capture-mode diagnostic: report the relief extent so the displacement exaggeration can be
        // calibrated against the actual elevation range at the captured tick.
        if (!string.IsNullOrEmpty(_capturePath) && elevByCell.Length > 0)
        {
            double emin = double.MaxValue, emax = double.MinValue;
            foreach (var e in elevByCell) { if (e < emin) emin = e; if (e > emax) emax = e; }
            GD.Print($"[GlobeView] relief extent @tick {tick}: elevation [{emin:F1}, {emax:F1}] m -> radial displace " +
                     $"[{emin * DisplacementExaggeration:F4}, {emax * DisplacementExaggeration:F4}] (×2 scale -> world ×2).");
        }

        EnsureCapInstances(caps.Count);

        float cellCount = Math.Max(_snapshot.CellCount, 1);
        for (int i = 0; i < caps.Count; i++)
        {
            var mesh = BuildCapMesh(caps[i], elevByCell, cellCount);
            var inst = _capInstances[i];
            inst.Mesh = mesh;
            inst.Name = $"PlateCap{caps[i].PlateId}";
        }
    }

    // Grow/shrink the pool of cap MeshInstance3D nodes to match the plate count (built once; topology is
    // tick-invariant so the count never actually changes after the first tick).
    private void EnsureCapInstances(int count)
    {
        if (_capRoot is null) return;
        while (_capInstances.Count < count)
        {
            var inst = new MeshInstance3D { MaterialOverride = _material };
            _capRoot.AddChild(inst);
            _capInstances.Add(inst);
        }
        for (int i = _capInstances.Count - 1; i >= count; i--)
        {
            _capInstances[i].QueueFree();
            _capInstances.RemoveAt(i);
        }
    }

    // Turn one watertight GlobeSurface cap into a BLOCKY (flat-shaded) ArrayMesh: for each triangle emit
    // 3 vertices at its (shared, watertight) corner POSITIONS, each carrying the triangle's single FLAT
    // normal. Positions come from the shared-vertex Positions (one averaged height per corner) so even
    // though verts are tripled for flat shading, adjacent triangles meet EXACTLY -> no cracks.
    //   uv.x  = plateId            (drives per-plate rotation in the shader)
    //   uv.y  = (cellId+0.5)/cells (tectonic-type texture lookup for this face's cell)
    //   uv2.x = per-cell elevation (biome ramp — crisp per-cell colour)
    private static ArrayMesh BuildCapMesh(PlateCap cap, double[] elevByCell, float cellCount)
    {
        var surface = cap.Surface;
        int triCount = surface.TriangleCount;
        int vertCount = triCount * 3;

        var verts = new Vector3[vertCount];
        var normals = new Vector3[vertCount];
        var uv = new Vector2[vertCount];
        var uv2 = new Vector2[vertCount];

        float plateId = cap.PlateId;
        for (int t = 0; t < triCount; t++)
        {
            int i0 = surface.Triangles[(t * 3) + 0];
            int i1 = surface.Triangles[(t * 3) + 1];
            int i2 = surface.Triangles[(t * 3) + 2];

            var flat = ToV3(surface.FlatNormals[t]);

            int cellId = cap.CellIds[t];
            float u = (cellId + 0.5f) / cellCount;
            float elev = (cellId >= 0 && cellId < elevByCell.Length) ? (float)elevByCell[cellId] : 0f;

            int b = t * 3;
            verts[b + 0] = ToV3(surface.Positions[i0]);
            verts[b + 1] = ToV3(surface.Positions[i1]);
            verts[b + 2] = ToV3(surface.Positions[i2]);
            normals[b + 0] = flat; normals[b + 1] = flat; normals[b + 2] = flat;
            uv[b + 0] = new Vector2(plateId, u); uv[b + 1] = new Vector2(plateId, u); uv[b + 2] = new Vector2(plateId, u);
            uv2[b + 0] = new Vector2(elev, 0f); uv2[b + 1] = new Vector2(elev, 0f); uv2[b + 2] = new Vector2(elev, 0f);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uv;
        arrays[(int)Mesh.ArrayType.TexUV2] = uv2;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

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

    private void UpdateRegimeThermalTint(long tick)
    {
        if (_mantleMaterial is null) return;

        if (!string.Equals(_currentRegimeId, "magma-ocean", StringComparison.Ordinal)
            || _magmaSurfaceTemperatureKAt is null
            || _thermalSourceUnavailable)
        {
            _mantleMaterial.AlbedoColor = DefaultMantleAlbedo;
            return;
        }

        try
        {
            var temperature = _magmaSurfaceTemperatureKAt(tick);
            _mantleMaterial.AlbedoColor = temperature is null
                ? DefaultMantleAlbedo
                : MagmaAlbedoForTemperature(temperature.Value);
        }
        catch (Exception ex)
        {
            _thermalSourceUnavailable = true;
            _mantleMaterial.AlbedoColor = DefaultMantleAlbedo;
            GD.PushWarning($"[GlobeView] magma thermal field fetch failed: {ex.Message}");
        }
    }

    private static Color MagmaAlbedoForTemperature(double temperatureK)
    {
        const double coolK = 1300.0;
        const double hotK = 2700.0;
        var t = (float)Math.Clamp((temperatureK - coolK) / (hotK - coolK), 0.0, 1.0);

        var basalt = new Color(0.22f, 0.10f, 0.08f);
        var ember = new Color(0.72f, 0.16f, 0.04f);
        var lava = new Color(1.00f, 0.46f, 0.08f);
        return t < 0.55f
            ? basalt.Lerp(ember, t / 0.55f)
            : ember.Lerp(lava, (t - 0.55f) / 0.45f);
    }


    private static long ParseEnvLong(string name, long fallback)
    {
        var raw = System.Environment.GetEnvironmentVariable(name);
        return long.TryParse(raw, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
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
    private static readonly Color DefaultMantleAlbedo = new(0.10f, 0.14f, 0.26f);

    private static StandardMaterial3D BuildMantleMaterial()
        => new()
        {
            AlbedoColor = DefaultMantleAlbedo,
            Roughness = 1.0f,
        };

    private static MeshInstance3D BuildMantle(StandardMaterial3D material) => new()
    {
        Name = "Mantle",
        Mesh = new SphereMesh { Radius = 0.78f, Height = 1.56f, RadialSegments = 48, Rings = 24 },
        Scale = Vector3.One * 2.0f,
        MaterialOverride = material,
    };

    private static Vector3 ToV3(CartesianPoint3 p) => new Vector3((float)p.X, (float)p.Y, (float)p.Z);
}
