using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Iryomera.ContentStudio.Components.Shared;

public abstract class StudioPage : ComponentBase
{
    [Inject] protected IStringLocalizer L { get; set; } = null!;
    [Inject] protected NavigationManager Nav { get; set; } = null!;
    protected Exception? Error { get; set; }
    protected string? Message { get; set; }
    protected bool Busy { get; set; }
    protected async Task Run(Func<Task> action)
    {
        if (Busy) return;
        Busy = true; Error = null; Message = null;
        try { await action(); Message = L["studio.saved"]; }
        catch (Exception exception) { Error = exception; }
        finally { Busy = false; }
    }
}
