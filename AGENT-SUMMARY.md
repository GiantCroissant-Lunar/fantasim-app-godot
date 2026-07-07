# Camera Phantom Host Root Cause

## Root Cause Chain

- `project/hosts/complete-app/project.godot:12-16` registers the addon autoload as
  `PhantomCameraManager`, matching the addon's constant in
  `project/hosts/complete-app/addons/phantom_camera/scripts/phantom_camera/phantom_camera_constants.gd:8-13`.
  The autoload name/path is not the defect.
- The host must be a child of a real camera: `phantom_camera_host.gd:261-277` only captures
  `camera_3d` and registers itself when its parent is `Camera3D`, and `phantom_camera_host.gd:906-918`
  writes the active pcam transform into that `camera_3d`. The seam already has this parentage at
  `project/plugins/App.Camera.Seam/CameraRig.cs:411-422`.
- Host/pcam attachment is manager + layer + priority driven: `phantom_camera_manager.gd:113-134`
  stores hosts and pcams, `phantom_camera_host.gd:364-398` accepts only matching `host_layers`,
  visible pcams, and the highest priority, and `phantom_camera_host.gd:672-680` marks the chosen pcam
  active. The main seam path uses layer bit 1 at `CameraRig.cs:350-353` and assigns it to both host
  and pcam at `CameraRig.cs:191-194` and `CameraRig.cs:420-422`.
- The failing globe camera depends on PhantomCamera3D ThirdPerson mode. The addon only creates the
  required `SpringArm3D` during `PhantomCamera3D._ready`: see
  `phantom_camera_3d.gd:914-941`. Its ThirdPerson follow logic then refuses to produce a new output
  until `_has_follow_spring_arm` is true at `phantom_camera_3d.gd:1149-1153`.
- Before this fix, the seam added the pcam to the tree before applying the pending globe
  `follow_mode`/`follow_target` configuration. If Godot ran `_ready` while `follow_mode` was still
  `NONE`, the addon skipped SpringArm creation permanently. Later C# orbit commands could update
  `CameraOrbitState`, but `set_third_person_rotation_degrees` only rotates `_follow_spring_arm`
  (`phantom_camera_3d.gd:1903-1910`), so there was no addon-driven camera movement. The host kept
  copying the stale pcam transform to the real camera (`phantom_camera_host.gd:878-918`), explaining
  the observed origin camera.

## Minimal Fix

- Changed only `project/plugins/App.Camera.Seam/CameraRig.cs`.
- New pcams are now fully configured before `AddChild`: initial transform, priority, host layer,
  camera resource, and any pending globe ThirdPerson config are applied at `CameraRig.cs:177-200`.
  This lets `phantom_camera_3d.gd:914-941` see `follow_mode == THIRD_PERSON` during `_ready` and
  create the SpringArm normally.
- Pending globe configuration is stored inside the seam and consumed pre-ready at
  `CameraRig.cs:78`, `CameraRig.cs:197-198`, and `CameraRig.cs:286-295`; immediate configuration for
  an already registered pcam still uses the same apply path at `CameraRig.cs:298-324`.
- Host `host_layers` is also assigned before the host enters the tree at `CameraRig.cs:415-422`.
  This is not the disproven parentage issue; it removes the same exported-var timing hazard for
  host registration.

## Verification Status

- I cannot run the exported windowed app in this environment.
- `dotnet build project/plugins/App.Camera.Seam/App.Camera.Seam.csproj --no-restore` failed because
  `project.assets.json` is missing for the Godot temp obj path.
- A normal `dotnet build project/plugins/App.Camera.Seam/App.Camera.Seam.csproj` entered restore and
  produced no further output in the restricted network sandbox, so I stopped treating local build as
  available verification.

## Lead Windowed-App Verification

Verify in the already exported windowed app:

- The real current `Camera3D` is no longer at the origin; it is positioned at the globe orbit spring
  distance.
- `camera.orbit {"yawDeg":80,"pitchDeg":-15}` changes pixels.
- Mouse drag orbits the globe and changes pixels.
- The view is outside the planet rather than inside the orange basal blanket.
