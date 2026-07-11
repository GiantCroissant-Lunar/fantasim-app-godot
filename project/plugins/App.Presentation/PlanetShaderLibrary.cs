using Godot;

namespace FantaSim.App.Presentation;

/// <summary>
/// Static shader source + lazily-built Shader/Material library for the planet presentation.
/// Extracted from PlanetPresentationBinder 2026-07-11
/// (vault/plans/2026-07-11-planet-presentation-binder-split-plan.md). Instance-scoped material
/// caches (mantle regime materials, the per-binder cutaway wedge override, the isosurface
/// instance materials) stay in the binder by design — only the shared static shader/material
/// singletons and the pure builder helpers move here.
/// </summary>
internal static class PlanetShaderLibrary
{
    // Both isosurface shaders render double-sided (cull_disabled) so the volumes read from any
    // angle; normals come from the anomaly-field gradient baked into the mesh. The translucent
    // variant is a separate shader because any static ALPHA write moves a material to the
    // transparent pipeline — the opaque cores must stay in the opaque pass (spec ingredient 4).
    public const string MantleIsosurfaceOpaqueShaderCode = @"
shader_type spatial;
render_mode cull_disabled;

uniform vec4 u_tint : source_color = vec4(0.2, 0.45, 0.9, 1.0);
uniform float u_emission_energy : hint_range(0.0, 8.0) = 1.0;

void fragment() {
    ALBEDO = u_tint.rgb;
    EMISSION = u_tint.rgb * u_emission_energy;
    ROUGHNESS = 0.85;
    METALLIC = 0.0;
}
";

    public const string MantleIsosurfaceTranslucentShaderCode = @"
shader_type spatial;
render_mode cull_disabled, depth_draw_never;

uniform vec4 u_tint : source_color = vec4(0.2, 0.45, 0.9, 1.0);
uniform float u_emission_energy : hint_range(0.0, 8.0) = 1.0;
uniform float u_alpha : hint_range(0.0, 1.0) = 0.25;

void fragment() {
    ALBEDO = u_tint.rgb;
    EMISSION = u_tint.rgb * u_emission_energy;
    ALPHA = u_alpha;
    ROUGHNESS = 0.85;
    METALLIC = 0.0;
}
";

    // magma-ocean mantle: emissive molten lava with a slowly drifting fBm churn. lava-hot tracks the
    // sibling GlobeView.MagmaAlbedoForTemperature lava endpoint so both render paths read as the same lava.
    public const string MagmaShaderCode = @"
shader_type spatial;
render_mode cull_disabled;

uniform vec4 u_lava_hot  : source_color = vec4(1.00, 0.46, 0.10, 1.0);
uniform vec4 u_lava_cool : source_color = vec4(0.16, 0.04, 0.03, 1.0);
uniform float u_emission_energy : hint_range(0.0, 8.0) = 1.6;
uniform float u_noise_scale = 2.6;
uniform float u_drift_speed = 0.05;

varying vec3 v_obj_pos;

vec3 hash3(vec3 p) {
    p = vec3(dot(p, vec3(127.1, 311.7, 74.7)),
             dot(p, vec3(269.5, 183.3, 246.1)),
             dot(p, vec3(113.5, 271.9, 124.6)));
    return -1.0 + 2.0 * fract(sin(p) * 43758.5453123);
}

float noise3(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    vec3 u = f * f * (3.0 - 2.0 * f);
    return mix(
        mix(mix(dot(hash3(i + vec3(0.0, 0.0, 0.0)), f - vec3(0.0, 0.0, 0.0)),
                dot(hash3(i + vec3(1.0, 0.0, 0.0)), f - vec3(1.0, 0.0, 0.0)), u.x),
            mix(dot(hash3(i + vec3(0.0, 1.0, 0.0)), f - vec3(0.0, 1.0, 0.0)),
                dot(hash3(i + vec3(1.0, 1.0, 0.0)), f - vec3(1.0, 1.0, 0.0)), u.x), u.y),
        mix(mix(dot(hash3(i + vec3(0.0, 0.0, 1.0)), f - vec3(0.0, 0.0, 1.0)),
                dot(hash3(i + vec3(1.0, 0.0, 1.0)), f - vec3(1.0, 0.0, 1.0)), u.x),
            mix(dot(hash3(i + vec3(0.0, 1.0, 1.0)), f - vec3(0.0, 1.0, 1.0)),
                dot(hash3(i + vec3(1.0, 1.0, 1.0)), f - vec3(1.0, 1.0, 1.0)), u.x), u.y),
        u.z);
}

float fbm(vec3 p) {
    float n  = noise3(p *  5.0) * 0.5000;
          n += noise3(p * 10.0) * 0.2500;
          n += noise3(p * 20.0) * 0.1250;
          n += noise3(p * 40.0) * 0.0625;
    return n;
}

void vertex() {
    v_obj_pos = VERTEX;
}

void fragment() {
    vec3 q = v_obj_pos * u_noise_scale + vec3(0.0, TIME * u_drift_speed, 0.0);
    float n = fbm(q);
    float t = smoothstep(-0.05, 0.45, n);
    vec3 col = mix(u_lava_cool.rgb, u_lava_hot.rgb, t);
    float vein = smoothstep(0.55, 0.80, n);
    col += u_lava_hot.rgb * vein * 0.60;

    ALBEDO = col;
    EMISSION = col * u_emission_energy + u_lava_hot.rgb * vein * u_emission_energy;
    ROUGHNESS = 0.62;
    METALLIC = 0.0;
}
";

    // stagnant-lid mantle: dark basaltic cooled crust, subtle noise-modulated albedo/roughness, and a
    // thin faintly-emissive crack band where the fBm crosses a threshold (cheap: one smoothstep pair).
    public const string StagnantShaderCode = @"
shader_type spatial;
render_mode cull_disabled;

uniform vec4 u_basalt_dark  : source_color = vec4(0.05, 0.05, 0.06, 1.0);
uniform vec4 u_basalt_light : source_color = vec4(0.20, 0.19, 0.21, 1.0);
uniform float u_crack_glow : hint_range(0.0, 2.0) = 0.16;
uniform float u_noise_scale = 3.2;

varying vec3 v_obj_pos;

vec3 hash3(vec3 p) {
    p = vec3(dot(p, vec3(127.1, 311.7, 74.7)),
             dot(p, vec3(269.5, 183.3, 246.1)),
             dot(p, vec3(113.5, 271.9, 124.6)));
    return -1.0 + 2.0 * fract(sin(p) * 43758.5453123);
}

float noise3(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    vec3 u = f * f * (3.0 - 2.0 * f);
    return mix(
        mix(mix(dot(hash3(i + vec3(0.0, 0.0, 0.0)), f - vec3(0.0, 0.0, 0.0)),
                dot(hash3(i + vec3(1.0, 0.0, 0.0)), f - vec3(1.0, 0.0, 0.0)), u.x),
            mix(dot(hash3(i + vec3(0.0, 1.0, 0.0)), f - vec3(0.0, 1.0, 0.0)),
                dot(hash3(i + vec3(1.0, 1.0, 0.0)), f - vec3(1.0, 1.0, 0.0)), u.x), u.y),
        mix(mix(dot(hash3(i + vec3(0.0, 0.0, 1.0)), f - vec3(0.0, 0.0, 1.0)),
                dot(hash3(i + vec3(1.0, 0.0, 1.0)), f - vec3(1.0, 0.0, 1.0)), u.x),
            mix(dot(hash3(i + vec3(0.0, 1.0, 1.0)), f - vec3(0.0, 1.0, 1.0)),
                dot(hash3(i + vec3(1.0, 1.0, 1.0)), f - vec3(1.0, 1.0, 1.0)), u.x), u.y),
        u.z);
}

float fbm(vec3 p) {
    float n  = noise3(p *  5.0) * 0.5000;
          n += noise3(p * 10.0) * 0.2500;
          n += noise3(p * 20.0) * 0.1250;
          n += noise3(p * 40.0) * 0.0625;
    return n;
}

void vertex() {
    v_obj_pos = VERTEX;
}

void fragment() {
    vec3 q = v_obj_pos * u_noise_scale;
    float n = fbm(q);
    float t = smoothstep(-0.10, 0.40, n);
    vec3 col = mix(u_basalt_dark.rgb, u_basalt_light.rgb, t);
    float crack = smoothstep(0.45, 0.55, n) - smoothstep(0.55, 0.70, n);
    col += u_basalt_light.rgb * crack * 0.40;

    ALBEDO = col;
    EMISSION = u_basalt_light.rgb * crack * u_crack_glow;
    ROUGHNESS = mix(0.88, 0.62, t);
    METALLIC = 0.0;
}
";

    // Hypsometric plate-cap shader (A2 + W3a cutaway discard): per-vertex COLOR carries the bare-crust
    // tint (dark basalt → rock brown → light rock, computed on the CPU with percentile normalization —
    // no water imagery; the hydrosphere lane owns that when it exists, per the no-sphere-costume rule);
    // UV2.x carries the volcanic-vent emission intensity (0 = none). Trench darkening and ridge
    // brightening are baked into the vertex COLOR on the CPU (CrustAccentMapper.Apply) so the shader
    // only needs albedo + a gated emission pass. Half-Lambert light keeps displaced relief readable.
    // W3a: the wedge discard (u_wedge_active) drops fragments whose object-space position direction
    // falls inside the dihedral wedge — the planet reads as a solid with a wedge cut out. Inactive
    // (u_wedge_active=false, width 0) = zero discard = today's render unchanged. The discard test
    // mirrors CutawayWedge.Contains (pure, unit-tested): project onto the perpendicular plane, measure
    // azimuth via atan2 against a basis derived the same way as the C# model.
    // Godot 4 docs: COLOR (vec4, auto-populated from ArrayType.Color, no flag on ShaderMaterial) and
    // UV2 (vec2, auto-populated from ArrayType.TexUV2); EMISSION is out-vec3 in fragment().
    public const string HypsoPlateShaderCode = @"
shader_type spatial;
render_mode cull_disabled;

uniform vec4 u_volcanic_glow : source_color = vec4(1.0, 0.42, 0.10, 1.0);
uniform float u_volcanic_energy : hint_range(0.0, 8.0) = 1.4;
uniform float u_albedo_gain : hint_range(0.5, 2.0) = 1.0;
uniform float u_albedo_ceiling : hint_range(0.1, 1.0) = 1.0;
uniform float u_light_floor : hint_range(0.0, 1.0) = 0.08;
uniform float u_wrap_strength : hint_range(0.0, 1.0) = 1.0;
uniform float u_light_contrast : hint_range(0.5, 2.0) = 1.0;
uniform vec3 u_color_balance = vec3(1.0, 1.0, 1.0);

// W3a cutaway wedge (inactive by default; zero discard when u_wedge_active is false).
uniform bool u_wedge_active = false;
uniform vec3 u_wedge_axis = vec3(0.0, 0.0, 1.0);
uniform vec3 u_wedge_reference = vec3(1.0, 0.0, 0.0);
uniform vec3 u_wedge_reference_cross = vec3(0.0, 1.0, 0.0);
uniform float u_wedge_start_rad = 0.0;
uniform float u_wedge_width_rad = 0.0;

const float TWO_PI = 6.28318530718;

// Wedge test needs the MODEL-space direction: in fragment() VERTEX is VIEW-space, so testing it
// there would make the wedge camera-relative (it would swing with the camera instead of cutting
// the planet). Capture object-space VERTEX in vertex() — where it IS model space — via a varying.
varying vec3 v_wedge_obj;

void vertex() {
    v_wedge_obj = VERTEX;
}

float wedge_azimuth(vec3 dir) {
    vec3 proj = dir - dot(dir, u_wedge_axis) * u_wedge_axis;
    float pl = length(proj);
    if (pl < 1e-7) return -1.0;
    vec3 unit = proj / pl;
    float x = dot(unit, u_wedge_reference);
    float y = dot(unit, u_wedge_reference_cross);
    float a = atan(y, x);
    if (a < 0.0) a += TWO_PI;
    return a;
}

bool wedge_contains(float azimuth) {
    if (azimuth < 0.0) return false;
    float end = u_wedge_start_rad + u_wedge_width_rad;
    if (end <= TWO_PI) {
        return azimuth >= u_wedge_start_rad && azimuth < end;
    }
    return azimuth >= u_wedge_start_rad || azimuth < (end - TWO_PI);
}

void fragment() {
    if (u_wedge_active) {
        vec3 dir = normalize(v_wedge_obj);
        float az = wedge_azimuth(dir);
        if (wedge_contains(az)) {
            discard;
        }
    }
    ALBEDO = clamp(COLOR.rgb * u_color_balance * u_albedo_gain, vec3(0.0), vec3(u_albedo_ceiling));
    float vent = UV2.x;
    if (vent > 0.001) {
        EMISSION = u_volcanic_glow.rgb * vent * u_volcanic_energy;
    }
    ROUGHNESS = 0.92;
    METALLIC = 0.0;
}

void light() {
    float ndotl = dot(normalize(NORMAL), normalize(LIGHT));
    float lambert = max(ndotl, 0.0);
    float wrapped = ndotl * 0.5 + 0.5;
    wrapped *= wrapped;
    float lit = mix(lambert, wrapped, u_wrap_strength);
    lit = pow(max(lit, u_light_floor), u_light_contrast);
    DIFFUSE_LIGHT += ALBEDO * LIGHT_COLOR * ATTENUATION * lit;
}
";

    // Atmosphere limb-glow shader (W2): a fresnel rim on a shell slightly larger than the surface.
    // The rim glows only at grazing angles (the limb) and vanishes face-on, so it never occludes the
    // surface or the label. Godot 4 docs grounding:
    //   - render_mode blend_add: additive blend (source added to destination; the rim only ADDS light,
    //     never darkens/occludes). Spatial Shader reference -> render_mode blend options.
    //   - depth_draw_never: the shell writes no depth, so it cannot hide the surface behind it.
    //   - unshaded: skip lighting; ALBEDO is the direct output color (the rim is pure glow, not lit).
    //   - cull_disabled: render both faces (house idiom; the near-hemisphere carries the fresnel).
    //   - NORMAL (view-space surface normal) and VIEW (fragment->camera direction, view space) are
    //     fragment() built-ins; dot(NORMAL, VIEW) peaks face-on, so (1 - dot) peaks at the limb.
    //   - source_color / hint_range uniform hints match the sibling shaders. RenderPriority (set on
    //     the ShaderMaterial in BuildAtmosphereRim) draws this after the opaque surface.
    public const string AtmosphereRimShaderCode = @"
shader_type spatial;
render_mode cull_disabled, blend_add, depth_draw_never, unshaded;

uniform vec4 u_tint : source_color = vec4(0.46, 0.68, 1.0, 1.0);
uniform float u_intensity : hint_range(0.0, 1.0) = 0.5;
// Falloff exponent: how tightly the glow hugs the limb. 3.0 washed an additive tint over most of
// the disk (2026-07-03 world-view finding: the whole planet read navy); 6.0 confines it to a rim.
uniform float u_falloff : hint_range(1.0, 12.0) = 6.0;

void fragment() {
    float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), u_falloff);
    ALBEDO = u_tint.rgb * (fresnel * u_intensity);
}
";

    private static Shader? _magmaShader;
    private static Shader? _stagnantShader;
    private static Shader? _hypsoPlateShader;
    private static Shader? _atmosphereRimShader;
    private static Material? _hypsoPlateMaterial;

    // M-B: darker unlit material for the solid-crust BOTTOM + SIDE WALLS — distinct from the
    // attributed surface so the slab silhouette reads as thickness, not as more surface. Unlit +
    // cull_disabled matches the cutaway render_mode; lit + real normals is a future refinement.
    public static readonly Material ExplodedCrustDarkMaterial = new StandardMaterial3D
    {
        AlbedoColor = new Color(0.12f, 0.10f, 0.09f),
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    public static Shader MagmaShader => _magmaShader ??= new Shader { Code = MagmaShaderCode };
    public static Shader StagnantShader => _stagnantShader ??= new Shader { Code = StagnantShaderCode };
    public static Shader HypsoPlateShader => _hypsoPlateShader ??= new Shader { Code = HypsoPlateShaderCode };
    public static Shader AtmosphereRimShader => _atmosphereRimShader ??= new Shader { Code = AtmosphereRimShaderCode };

    public static Material HypsoPlateMaterial => _hypsoPlateMaterial ??= new ShaderMaterial { Shader = HypsoPlateShader };

    public static ShaderMaterial BuildIsosurfaceMaterial(Color tint, float emission, float alpha, int priority)
    {
        var material = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = alpha >= 1.0f ? MantleIsosurfaceOpaqueShaderCode : MantleIsosurfaceTranslucentShaderCode,
            },
            RenderPriority = priority,
        };
        material.SetShaderParameter("u_tint", tint);
        material.SetShaderParameter("u_emission_energy", emission);
        if (alpha < 1.0f)
            material.SetShaderParameter("u_alpha", alpha);
        return material;
    }

    public static ShaderMaterial BuildMagmaMantleMaterial() => new() { Shader = MagmaShader };

    public static ShaderMaterial BuildStagnantMantleMaterial() => new() { Shader = StagnantShader };

    public static StandardMaterial3D BuildBaseMantleMaterial() =>
        new()
        {
            AlbedoColor = new Color(0.02f, 0.20f, 0.28f),
            Roughness = 0.82f,
            Metallic = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
}
