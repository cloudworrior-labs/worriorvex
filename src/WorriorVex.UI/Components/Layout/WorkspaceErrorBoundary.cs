using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;

namespace WorriorVex.UI.Components.Layout;

/// <summary>
/// Catches an exception that escapes a component. Without this the renderer stops and the window keeps
/// its last picture with nothing responding; with it the error is logged and shown, and the person can
/// carry on.
/// </summary>
public sealed class WorkspaceErrorBoundary : ErrorBoundary
{
    [Inject] private ILogger<WorkspaceErrorBoundary> Logger { get; set; } = default!;

    protected override Task OnErrorAsync(Exception exception)
    {
        Logger.LogCritical(exception, "Unhandled exception in the interface");
        return Task.CompletedTask;
    }
}
