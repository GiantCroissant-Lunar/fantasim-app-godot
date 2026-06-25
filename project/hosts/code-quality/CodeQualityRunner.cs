using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using R3;

namespace CodeQuality;

// Headless code-quality / integration runner. Boots under `godot --headless`, runs the checks,
// and quits with 0 (passed) or 1 (failed) so CI / `task` can gate. Step B smoke: prove the
// vendored R3.Godot addon + the FrameProviderDispatcher autoload tick the real
// GodotFrameProvider.Process so Observable.NextFrame advances under --headless -- the real frame
// source ReloadPolicy needs (vs S2a's FakeFrameProvider). Step C runs the real reload check here.
public partial class CodeQualityRunner : Node
{
    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        var exitCode = 0;
        try
        {
            GD.Print("[code-quality] headless host up; awaiting real GodotFrameProvider frames...");
            await Observable.NextFrame(GodotFrameProvider.Process).FirstAsync(CancellationToken.None);
            await Observable.NextFrame(GodotFrameProvider.Process).FirstAsync(CancellationToken.None);
            GD.Print("[code-quality] GodotFrameProvider.Process advanced (Step B addon smoke OK)");
        }
        catch (Exception ex)
        {
            exitCode = 1;
            GD.PrintErr($"[code-quality] FAILED: {ex}");
        }

        GD.Print($"[code-quality] done, exitCode={exitCode}");
        GetTree().Quit(exitCode);
    }
}
