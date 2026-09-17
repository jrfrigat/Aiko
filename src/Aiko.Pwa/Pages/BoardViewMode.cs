namespace Aiko.Pwa.Pages;

/// <summary>
/// Which card kinds a board page draws. The three board routes (<c>/board</c>, <c>/stories</c>,
/// <c>/tasks</c>) differ only by this, so they share one component.
/// </summary>
public enum BoardViewMode
{
    /// <summary>Stories and tasks together, one section each.</summary>
    Combined,

    /// <summary>Stories only.</summary>
    Stories,

    /// <summary>Tasks only.</summary>
    Tasks
}
