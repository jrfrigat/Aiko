using Microsoft.JSInterop;

namespace Aiko.Pwa.Services;

/// <summary>
/// The browser's own yes/no prompt, asked in a way the page can survive.
/// </summary>
/// <remarks>
/// A browser that cannot run the prompt - a stale client without the script, a circuit that is already
/// gone, or a page that is still prerendering - must not decide on the person's behalf. The answer is
/// therefore always "no": the caller keeps the edits, or skips the action, instead of losing something
/// nobody agreed to lose.
/// </remarks>
internal static class BrowserPrompt
{
    /// <summary>Asks a yes/no question, answering "no" when the prompt cannot be shown.</summary>
    public static async Task<bool> ConfirmAsync(IJSRuntime js, string message)
    {
        try
        {
            return await js.InvokeAsync<bool>("confirm", message);
        }
        catch (JSDisconnectedException)
        {
            return false;
        }
        catch (JSException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
