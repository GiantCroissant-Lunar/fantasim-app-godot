using FantaSim.App.World.Globe;
using Godot;

namespace FantaSim.App.Presentation;

internal sealed partial class PlateSurfaceRenderer : Node3D
{
    private readonly List<RenderSlot> _slots = new();
    private Material? _material;

    public PlateSurfaceRenderer()
    {
        Name = "PlateSurface";
        Scale = Vector3.One * 2.0f;
    }

    public void SetMeshes(IReadOnlyList<PlateCapMeshDto> meshes, Material material)
    {
        ArgumentNullException.ThrowIfNull(meshes);
        ArgumentNullException.ThrowIfNull(material);

        _material = material;
        EnsureSlotCount(meshes.Count);

        for (int i = 0; i < meshes.Count; i++)
        {
            var dto = meshes[i];
            var slot = _slots[i];
            slot.PlateId = dto.PlateId;
            slot.Mesh = BuildArrayMesh(dto);
            slot.Active = true;
        }

        for (int i = meshes.Count; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            slot.PlateId = null;
            slot.Mesh = null;
            slot.Active = false;
        }

        if (IsInsideTree())
            ApplySlotsToRenderingServer();
    }

    public override void _EnterTree()
    {
        ApplySlotsToRenderingServer();
    }

    public override void _ExitTree()
    {
        FreeRenderingServerInstances();
    }

    public override void _Process(double delta)
    {
        UpdateInstanceState();
    }

    public void ReleaseRenderingResources()
    {
        FreeRenderingServerInstances();
        _material = null;
        for (int i = 0; i < _slots.Count; i++)
        {
            _slots[i].PlateId = null;
            _slots[i].Mesh = null;
            _slots[i].Active = false;
        }
    }

    private void EnsureSlotCount(int count)
    {
        while (_slots.Count < count)
            _slots.Add(new RenderSlot());
    }

    private void ApplySlotsToRenderingServer()
    {
        if (_material is null)
            return;

        var world = GetWorld3D();
        var scenario = world.Scenario;
        var materialRid = _material.GetRid();

        for (int i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            if (!slot.Active || slot.Mesh is null)
            {
                HideSlot(slot);
                continue;
            }

            EnsureInstanceRid(slot, scenario);
            RenderingServer.InstanceSetBase(slot.InstanceRid, slot.Mesh.GetRid());
            RenderingServer.InstanceGeometrySetMaterialOverride(slot.InstanceRid, materialRid);
            RenderingServer.InstanceSetTransform(slot.InstanceRid, GlobalTransform);
            RenderingServer.InstanceSetVisible(slot.InstanceRid, IsVisibleInTree());
        }
    }

    private void EnsureInstanceRid(RenderSlot slot, Rid scenario)
    {
        if (slot.InstanceRid.IsValid)
            return;

        // Godot 4.7 RenderingServer instances must be attached to a base resource and scenario,
        // and direct RIDs must be freed explicitly.
        // Source: https://docs.godotengine.org/en/stable/classes/class_renderingserver.html
        slot.InstanceRid = RenderingServer.InstanceCreate();
        RenderingServer.InstanceSetScenario(slot.InstanceRid, scenario);
    }

    private static void HideSlot(RenderSlot slot)
    {
        if (!slot.InstanceRid.IsValid)
            return;

        RenderingServer.InstanceSetVisible(slot.InstanceRid, false);
        RenderingServer.InstanceSetBase(slot.InstanceRid, default);
    }

    private void UpdateInstanceState()
    {
        bool visible = IsVisibleInTree();
        for (int i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            if (slot.Active && slot.InstanceRid.IsValid)
            {
                RenderingServer.InstanceSetTransform(slot.InstanceRid, GlobalTransform);
                RenderingServer.InstanceSetVisible(slot.InstanceRid, visible);
            }
        }
    }

    private void FreeRenderingServerInstances()
    {
        for (int i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            if (!slot.InstanceRid.IsValid)
                continue;

            RenderingServer.InstanceSetVisible(slot.InstanceRid, false);
            RenderingServer.FreeRid(slot.InstanceRid);
            slot.InstanceRid = default;
        }
    }

    private static ArrayMesh BuildArrayMesh(PlateCapMeshDto dto)
    {
        var vertices = new Vector3[dto.VertexCount];
        var normals = new Vector3[dto.VertexCount];
        var colors = new Color[dto.VertexCount];
        var uv2 = new Vector2[dto.VertexCount];

        for (int i = 0; i < dto.VertexCount; i++)
        {
            int v3 = i * 3;
            vertices[i] = new Vector3(
                dto.Positions[v3 + 0],
                dto.Positions[v3 + 1],
                dto.Positions[v3 + 2]);
            normals[i] = new Vector3(
                dto.Normals[v3 + 0],
                dto.Normals[v3 + 1],
                dto.Normals[v3 + 2]);
            colors[i] = new Color(
                dto.Colors[v3 + 0],
                dto.Colors[v3 + 1],
                dto.Colors[v3 + 2]);

            int uv = i * 2;
            uv2[i] = new Vector2(dto.Uv2[uv + 0], dto.Uv2[uv + 1]);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.TexUV2] = uv2;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private sealed class RenderSlot
    {
        public int? PlateId { get; set; }
        public ArrayMesh? Mesh { get; set; }
        public Rid InstanceRid { get; set; }
        public bool Active { get; set; }
    }
}
