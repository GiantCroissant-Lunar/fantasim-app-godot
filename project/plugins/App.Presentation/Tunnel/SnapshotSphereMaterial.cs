using System;
using Godot;

namespace FantaSim.App.Presentation.Tunnel;

/// <summary>
/// Owns one sphere-local material and its emphasis uniforms. The preview texture is assigned to the
/// shader verbatim; desaturation is performed while sampling, so cached ImageTexture identity never
/// changes when focus emphasis changes.
/// </summary>
internal sealed class SnapshotSphereMaterial : IDisposable
{
    private const string TextureParameter = "albedo_texture";
    private const string SaturationParameter = "saturation";
    private const string ValueParameter = "value";

    // Godot 4 spatial shader uniforms and source_color texture hints:
    // https://docs.godotengine.org/en/4.7/tutorials/shaders/shader_reference/spatial_shader.html
    // https://docs.godotengine.org/en/4.7/tutorials/shaders/shader_reference/shading_language.html
    private const string ShaderCode = """
        shader_type spatial;

        uniform sampler2D albedo_texture : source_color, filter_linear_mipmap;
        uniform float saturation : hint_range(0.0, 1.0) = 1.0;
        uniform float value : hint_range(0.0, 1.0) = 1.0;

        void fragment() {
            vec3 color = texture(albedo_texture, UV).rgb;
            float luminance = dot(color, vec3(0.2126, 0.7152, 0.0722));
            ALBEDO = mix(vec3(luminance), color, saturation) * value;
            ROUGHNESS = 0.72;
            METALLIC = 0.02;
        }
        """;

    private readonly Shader _shader;
    private bool _disposed;

    internal SnapshotSphereMaterial()
    {
        _shader = new Shader { Code = ShaderCode };
        Material = new ShaderMaterial { Shader = _shader };
        SetTexture(null);
        SetEmphasis(new TunnelSphereTone(Saturation: 1f, Value: 1f));
    }

    internal ShaderMaterial Material { get; }

    internal ImageTexture? Texture { get; private set; }

    internal bool IsAlive
        => !_disposed
           && GodotObject.IsInstanceValid(Material)
           && GodotObject.IsInstanceValid(_shader);

    internal void SetTexture(ImageTexture? texture)
    {
        if (_disposed)
            return;

        Texture = texture;
        Material.SetShaderParameter(
            TextureParameter,
            texture is null ? default(Variant) : texture);
    }

    internal void SetEmphasis(TunnelSphereTone tone)
    {
        if (_disposed)
            return;

        Material.SetShaderParameter(SaturationParameter, tone.Saturation);
        Material.SetShaderParameter(ValueParameter, tone.Value);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        SetTexture(null);
        _disposed = true;
        if (GodotObject.IsInstanceValid(Material))
        {
            Material.Shader = null;
            Material.Dispose();
        }
        if (GodotObject.IsInstanceValid(_shader))
            _shader.Dispose();
    }
}
