#[compute]
#version 450

// gpu-demo: doubles every uint in the bound storage buffer.
// Dispatched through App.GpuCompute (resident RenderingDevice seam); the readback
// proves an end-to-end resource-asset -> dispatch -> result round trip.
//
// The element count is supplied via a tiny second storage buffer (set 0, binding 1)
// rather than data.length(). On Godot's Metal backend (macOS), SPIR-V->MSL cross-compile
// of an unsized-array .length() emits an illegal buffer-size-constants reinterpret_cast
// that the Metal compiler rejects at pipeline creation; an explicit count sidesteps it and
// is portable across Vulkan/D3D12/Metal.

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

layout(set = 0, binding = 0, std430) buffer DataBuffer {
    uint data[];
}
data_buffer;

layout(set = 0, binding = 1, std430) readonly buffer CountBuffer {
    uint count;
}
count_buffer;

void main() {
    uint index = gl_GlobalInvocationID.x;
    if (index >= count_buffer.count) {
        return;
    }
    data_buffer.data[index] = data_buffer.data[index] * 2u;
}
