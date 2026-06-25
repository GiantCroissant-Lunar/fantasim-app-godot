using Godot;

namespace CodeQuality;

// Headless code-quality / integration runner. Boots under `godot --headless`, runs the
// registered checks, and quits with 0 (all passed) or 1 (any failed) so CI / `task` can gate.
// Step A is a smoke check (host boots, exits 0). Step B adds the real reload-collection
// integration check (real ViewHost + BundleHost + ReloadPolicy + GodotFrameProvider.Process).
public partial class CodeQualityRunner : Node
{
    public override void _Ready()
    {
        var exitCode = 0;
        GD.Print("[code-quality] headless host up (Step A smoke)");

        // TODO Step B: run the reload-collection integration check here and set exitCode.

        GD.Print($"[code-quality] done, exitCode={exitCode}");
        GetTree().Quit(exitCode);
    }
}
