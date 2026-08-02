// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails;

/// <summary>States whether a message's MIME could be read, and what stopped the reader when it could not.</summary>
public enum EmailMimeExtractionOutcome
{
    /// <summary>The message was read and its normalized metadata is present.</summary>
    Extracted = 0,

    /// <summary>The bytes do not parse as a MIME message, which is expected of real mail rather than exceptional.</summary>
    MalformedContent = 1,

    /// <summary>The message declares more parts than the configured limit, so reading it was abandoned.</summary>
    PartCountLimitExceeded = 2,

    /// <summary>The message nests multiparts deeper than the configured limit, so reading it was abandoned.</summary>
    NestingDepthLimitExceeded = 3,
}

/// <summary>Carries the metadata read from a message, or the reason none could be read.</summary>
/// <remarks>
/// Failure is a result rather than an exception because badly formed mail is expected: a message nobody can parse must
/// be recorded and stepped over, leaving the batch and the folder checkpoint to continue.
/// </remarks>
public sealed record EmailMimeExtractionResult
{
    private EmailMimeExtractionResult(EmailMimeExtractionOutcome outcome, ExtractedEmailMetadata? metadata)
    {
        this.Outcome = outcome;
        this.Metadata = metadata;
    }

    /// <summary>Gets what happened.</summary>
    public EmailMimeExtractionOutcome Outcome { get; }

    /// <summary>Gets the metadata, which is present exactly when <see cref="Outcome" /> is <see cref="EmailMimeExtractionOutcome.Extracted" />.</summary>
    public ExtractedEmailMetadata? Metadata { get; }

    /// <summary>Reports a message whose MIME was read.</summary>
    /// <param name="metadata">The normalized metadata.</param>
    /// <returns>An extracted result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="metadata" /> is <see langword="null" />.</exception>
    public static EmailMimeExtractionResult Extracted(ExtractedEmailMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        return new EmailMimeExtractionResult(EmailMimeExtractionOutcome.Extracted, metadata);
    }

    /// <summary>Reports a message the reader could not parse.</summary>
    /// <returns>A malformed result.</returns>
    public static EmailMimeExtractionResult MalformedContent() =>
        new(EmailMimeExtractionOutcome.MalformedContent, metadata: null);

    /// <summary>Reports a message abandoned because it declares more parts than the configured limit.</summary>
    /// <returns>A part-count failure.</returns>
    public static EmailMimeExtractionResult PartCountLimitExceeded() =>
        new(EmailMimeExtractionOutcome.PartCountLimitExceeded, metadata: null);

    /// <summary>Reports a message abandoned because it nests deeper than the configured limit.</summary>
    /// <returns>A nesting-depth failure.</returns>
    public static EmailMimeExtractionResult NestingDepthLimitExceeded() =>
        new(EmailMimeExtractionOutcome.NestingDepthLimitExceeded, metadata: null);
}
