// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Application.Emails;
using MailMcp.Domain.Emails;

namespace MailMcp.Application.EmailContent;

/// <summary>States whether a seen-preserving fetch returned raw MIME, and what stopped it when it did not.</summary>
public enum RemoteEmailContentFetchOutcome
{
    /// <summary>The content was fetched and is present.</summary>
    Retrieved = 0,

    /// <summary>The payload grew past the configured limit while it was being read, so the fetch was abandoned.</summary>
    ExceededSizeLimit = 1,
}

/// <summary>Carries the raw MIME a fetch returned, or the reason none was returned.</summary>
/// <remarks>
/// An oversized email is a result rather than an exception because the caller acts on it directly: the occurrence is
/// recorded as <see cref="StoredEmailContentAvailability.ExceededSizeLimit" /> and stepped over, leaving the batch and
/// the folder checkpoint to continue, exactly as <see cref="EmailMimeExtractionResult" /> does one step later for an
/// email nobody can parse.
/// <para>
/// The limit is reached while the stream is being read, so it catches a server whose advertised size understated the
/// payload as well as one that reported it honestly.
/// </para>
/// </remarks>
public sealed record RemoteEmailContentFetchResult
{
    private RemoteEmailContentFetchResult(RemoteEmailContentFetchOutcome outcome, RemoteEmailContent? content)
    {
        this.Outcome = outcome;
        this.Content = content;
    }

    /// <summary>Gets what happened.</summary>
    public RemoteEmailContentFetchOutcome Outcome { get; }

    /// <summary>Gets the content, which is present exactly when <see cref="Outcome" /> is <see cref="RemoteEmailContentFetchOutcome.Retrieved" />.</summary>
    public RemoteEmailContent? Content { get; }

    /// <summary>Reports content the mailbox served in full.</summary>
    /// <param name="content">The raw MIME content of the occurrence.</param>
    /// <returns>A retrieved result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content" /> is <see langword="null" />.</exception>
    public static RemoteEmailContentFetchResult Retrieved(RemoteEmailContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new RemoteEmailContentFetchResult(RemoteEmailContentFetchOutcome.Retrieved, content);
    }

    /// <summary>Reports a payload abandoned because it grew past the configured limit.</summary>
    /// <returns>A size-limit result.</returns>
    public static RemoteEmailContentFetchResult ExceededSizeLimit() =>
        new(RemoteEmailContentFetchOutcome.ExceededSizeLimit, content: null);
}
