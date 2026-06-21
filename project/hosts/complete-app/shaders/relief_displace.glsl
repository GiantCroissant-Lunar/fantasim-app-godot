#[compute]
#version 450

// relief_displace (sub-project C.1): per-cell radial displacement + flat face normals.
//
// The globe mesh is 3 independent verts per cell (a flat triangle per geodesic cell, NOT a
// shared-vertex sphere). One invocation handles ONE triangle: it reads the 3 base unit-sphere
// corners + their per-cell elevation, pushes each corner radially out/in by elevation, and emits
// one OUTWARD-oriented face normal shared by all 3 corners. Flat-shading the faceted globe is the
// correct lighting model here (smooth normals would fight the faceted geometry).
//
//   displaced = base + normalize(base) * elevation * exaggeration
//   normal    = orient_outward(normalize(cross(d1 - d0, d2 - d0)))
//
// Counts/params arrive via a small storage buffer (set 0, binding 4), NOT array.length():
// on Godot's Metal backend (macOS) SPIR-V->MSL of an unsized-array .length() emits an illegal
// buffer-size reinterpret_cast the Metal compiler rejects at pipeline creation. (Same gotcha the
// gpu-smoke compute_double.glsl documents.) Portable across Vulkan / D3D12 / Metal.

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

// Base per-vertex positions (xyz packed, w unused), length = triCount * 3. Read-only.
layout(set = 0, binding = 0, std430) readonly buffer BasePosBuffer {
    vec4 base_pos[];
} base_pos_buffer;

// Per-vertex elevation (the cell's elevation replicated to its 3 verts), length = triCount * 3.
layout(set = 0, binding = 1, std430) readonly buffer ElevBuffer {
    float elev[];
} elev_buffer;

// OUT: displaced per-vertex positions (xyz, w = 1), length = triCount * 3.
layout(set = 0, binding = 2, std430) writeonly buffer OutPosBuffer {
    vec4 out_pos[];
} out_pos_buffer;

// OUT: per-vertex normals (xyz, w = 0), length = triCount * 3. All 3 verts of a tri share the face normal.
layout(set = 0, binding = 3, std430) writeonly buffer OutNrmBuffer {
    vec4 out_nrm[];
} out_nrm_buffer;

// Params: x = triangle count, y = displacement exaggeration (float bits). Sidesteps .length().
layout(set = 0, binding = 4, std430) readonly buffer ParamBuffer {
    uint tri_count;
    float exaggeration;
} param_buffer;

void main() {
    uint tri = gl_GlobalInvocationID.x;
    if (tri >= param_buffer.tri_count) {
        return;
    }

    uint i0 = tri * 3u;
    uint i1 = i0 + 1u;
    uint i2 = i0 + 2u;

    vec3 b0 = base_pos_buffer.base_pos[i0].xyz;
    vec3 b1 = base_pos_buffer.base_pos[i1].xyz;
    vec3 b2 = base_pos_buffer.base_pos[i2].xyz;

    float e0 = elev_buffer.elev[i0];
    float e1 = elev_buffer.elev[i1];
    float e2 = elev_buffer.elev[i2];

    float exag = param_buffer.exaggeration;

    vec3 d0 = b0 + normalize(b0) * (e0 * exag);
    vec3 d1 = b1 + normalize(b1) * (e1 * exag);
    vec3 d2 = b2 + normalize(b2) * (e2 * exag);

    // Flat face normal, oriented to point away from the planet center (robust to source winding).
    // NB: avoid the identifier 'centroid' — it is a reserved GLSL interpolation keyword and the
    // SPIR-V compiler rejects it as a variable name.
    vec3 n = cross(d1 - d0, d2 - d0);
    float nlen = length(n);
    n = nlen > 1e-8 ? n / nlen : normalize(d0);
    vec3 face_center = (d0 + d1 + d2) * (1.0 / 3.0);
    if (dot(n, face_center) < 0.0) {
        n = -n;
    }

    out_pos_buffer.out_pos[i0] = vec4(d0, 1.0);
    out_pos_buffer.out_pos[i1] = vec4(d1, 1.0);
    out_pos_buffer.out_pos[i2] = vec4(d2, 1.0);

    out_nrm_buffer.out_nrm[i0] = vec4(n, 0.0);
    out_nrm_buffer.out_nrm[i1] = vec4(n, 0.0);
    out_nrm_buffer.out_nrm[i2] = vec4(n, 0.0);
}
