using BoomHud.Godot.Runtime;
using Godot;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.Ui.Seam;

public static class EmbeddedNodeGraphRenderer
{
    public static IDisposable? TryBindReadOnly(GraphEdit graphEdit, object viewModel, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(graphEdit);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(logger);

        graphEdit.MouseFilter = Control.MouseFilterEnum.Stop;
        graphEdit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        graphEdit.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        var binder = BoomHudGraphEditBinder.TryBind(graphEdit, viewModel);
        if (binder is null)
            return null;

        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(graphEdit))
                return;

            GraphNodeVisualEnhancer.TryApply(graphEdit, viewModel, logger);
            MsaglGraphLayoutApplicator.TryApply(graphEdit, viewModel, logger);
            GraphAnnotationFrameEnhancer.TryApply(graphEdit, viewModel, logger);
        }).CallDeferred();

        return binder;
    }
}
