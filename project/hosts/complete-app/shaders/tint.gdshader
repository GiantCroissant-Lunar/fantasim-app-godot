shader_type spatial;

// gpu-demo: minimal spatial shader. Loaded directly as a Godot Shader resource and inspected
// through App.GpuShader (the resident seam reports its mode = "spatial"). Used by the
// FANTASIM_GPUSHADER_SMOKE inspect smoke; never compiled for dispatch.

uniform vec4 tint : source_color = vec4(0.4, 0.7, 1.0, 1.0);
uniform float strength : hint_range(0.0, 1.0) = 0.5;

void fragment() {
    ALBEDO = mix(ALBEDO, tint.rgb, strength);
}
