// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Application.EmailContent;

/// <summary>States whether stored MIME could be rendered for a reader.</summary>
public enum EmailContentRenderingOutcome
{
    /// <summary>The message was parsed and its rendering is present.</summary>
    Rendered = 0,

    /// <summary>Nothing a reader could be shown came out of the stored bytes.</summary>
    /// <remarks>
    /// The reasons a parse gives up — bytes that are not a MIME message, more parts than the configured limit, deeper
    /// nesting than it allows — are one outcome here rather than three, because this read acts identically on all of
    /// them: the local copy is unusable, the caller is told so with one stable code, and repair is requested. The
    /// distinction is still made where it changes a decision, in the extraction path that has to record why a message
    /// contributes nothing to search.
    /// </remarks>
    Unreadable = 1,
}

/// <summary>Carries what a parse of stored MIME produced, or the fact that it produced nothing usable.</summary>
/// <remarks>
/// Failure is a result rather than an exception for the reason it is in extraction: unreadable mail is expected, and a
/// read that meets it answers with a stable failure of its own instead of letting a parser exception travel through
/// code that cannot decide what it means.
/// </remarks>
public sealed record EmailContentRenderingResult
{
    private EmailContentRenderingResult(EmailContentRenderingOutcome outcome, EmailContentRendering? rendering)
    {
        this.Outcome = outcome;
        this.Rendering = rendering;
    }

    /// <summary>Gets what happened.</summary>
    public EmailContentRenderingOutcome Outcome { get; }

    /// <summary>Gets the rendering, which is present exactly when <see cref="Outcome" /> is <see cref="EmailContentRenderingOutcome.Rendered" />.</summary>
    public EmailContentRendering? Rendering { get; }

    /// <summary>Reports a message that was parsed.</summary>
    /// <param name="rendering">What the parse produced.</param>
    /// <returns>A rendered result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rendering" /> is <see langword="null" />.</exception>
    public static EmailContentRenderingResult Rendered(EmailContentRendering rendering)
    {
        ArgumentNullException.ThrowIfNull(rendering);

        return new EmailContentRenderingResult(EmailContentRenderingOutcome.Rendered, rendering);
    }

    /// <summary>Reports stored bytes that yielded no message a reader could be shown.</summary>
    /// <returns>An unreadable result.</returns>
    public static EmailContentRenderingResult Unreadable() =>
        new(EmailContentRenderingOutcome.Unreadable, rendering: null);
}
