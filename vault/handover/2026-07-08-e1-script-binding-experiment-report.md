VERDICT E1a: FAIL
VERDICT E1b: FAIL

## Scope

Experiment target: exported Godot 4.7 .NET macOS app. Payload assembly `E1Payload.dll` defines `PayloadNode : Node`; host assembly does not ship `E1Payload.dll` in exported app data. `payload.pck` is placed beside the exported executable and contains `res://bundles/payload/E1Payload.dll` plus `PayloadScene.tscn`.

I treated the exported run as decisive. Editor/source-project runs were used only for harness debugging.

## Exact Evidence

Godot binary:

```text
4.7.stable.mono.official.5b4e0cb0f
```

Payload PCK export stored the expected payload files:

```text
Storing File: res://bundles/payload/PayloadNode.cs
Storing File: res://bundles/payload/PayloadNode.cs.uid
Storing File: res://bundles/payload/E1Payload.dll
Storing File: res://bundles/payload/PayloadScene.tscn.remap
```

Final exported host data check showed `E1Payload.dll` was absent from the app payload; only host/loader assemblies were present:

```text
build/e1host-reflect.app/Contents/Resources/data_e1host_macos_arm64/E1Support.dll
build/e1host-reflect.app/Contents/Resources/data_e1host_macos_arm64/e1host.dll
build/e1host-reflect.app/Contents/Resources/data_e1host_macos_x86_64/E1Support.dll
build/e1host-reflect.app/Contents/Resources/data_e1host_macos_x86_64/e1host.dll
```

Decisive exported run:

```text
$ ./e1host --headless
Godot Engine v4.7.stable.mono.official.5b4e0cb0f - https://godotengine.org

ERROR: Condition "ret != noErr" is true. Returning: ""
   at: get_system_ca_certificates (platform/macos/os_macos.mm:1035)

exit code: 139
```

Expected markers such as `E1_BOOT_BEGIN`, `E1_PCK_LOAD`, `E1_ASSEMBLY_LOADED`, `E1A_RESULT`, and `E1B_RESULT` never printed.

Native crash report evidence from `~/Library/Logs/DiagnosticReports/e1host-2026-07-08-145422.ips`:

```text
"exception" : {"type":"EXC_BAD_ACCESS","signal":"SIGSEGV","subtype":"KERN_INVALID_ADDRESS at 0x0000000000000000"}
"termination" : {"code":11,"namespace":"SIGNAL","indicator":"Segmentation fault: 11"}
faultingThread: 0
frames included CoreCLR/JIT startup:
MethodTable::RunClassInitEx
MethodTable::DoRunClassInitThrowing
JIT_GetSharedNonGCStaticBase_Helper
```

Controls proving exported C# apps can run in this environment:

```text
$ ./e1smoke --headless
Godot Engine v4.7.stable.mono.official.5b4e0cb0f - https://godotengine.org
ERROR: Condition "ret != noErr" is true. Returning: ""
   at: get_system_ca_certificates (platform/macos/os_macos.mm:1035)
SMOKE_READY
exit code: 0
```

```text
$ ./e1host --headless   # same host reduced to a print-and-quit Bootstrap
Godot Engine v4.7.stable.mono.official.5b4e0cb0f - https://godotengine.org
ERROR: Condition "ret != noErr" is true. Returning: ""
   at: get_system_ca_certificates (platform/macos/os_macos.mm:1035)
E1_SIMPLE_READY
exit code: 0
```

## Mechanism Explanation

The exported app crashed natively before the host reached `Bootstrap._Ready`; therefore it never called `ProjectSettings.LoadResourcePack`, never extracted `E1Payload.dll`, and never called `Assembly.LoadFrom`.

I tried several host shapes:

- Payload referenced by host and then stripped from both exported data directories.
- Payload never referenced by host, so `E1Payload.dll` was absent by construction.
- Async bootstrap, synchronous bootstrap, tiny Node bootstrap plus helper in host assembly, and tiny Node bootstrap plus helper in a separate `E1Support.dll`.

The only exported host variants that ran were the ones with no loader/reflection support. The loader variants failed before any E1 marker, while the smoke/simple controls ran. This makes the result a real exported-run failure of the attempted Godot-facing PCK-delivery bootstrap, not a missing export-template block.

## Implications

For `common.pck`: do not move Godot-facing script assemblies out of the exported app based on this experiment. E1 did not produce evidence that a runtime-loaded, PCK-delivered Godot `Node` script assembly can be registered or scene-bound in an exported macOS .NET app.

Pure support assemblies remain a separate question. This E1 result specifically blocks treating `Godot.NET.Sdk` assemblies containing Godot script classes as safe common.pck residents.
