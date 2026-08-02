// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.GetEmailContent;

/// <summary>What one read produced for one of the emails it named.</summary>
/// <remarks>
/// <para>
/// Exactly one of <see cref="Content" /> and <see cref="Failure" /> is present, which the factories are what guarantee.
/// The identity is carried either way, so a caller pairs an outcome with the email it asked about rather than with the
/// position it happened to occupy.
/// </para>
/// <para>
/// The outcome exists per email because a read names several: one identifier this deployment cannot serve must cost the
/// caller that email and nothing else.
/// </para>
/// </remarks>
public sealed record EmailContentReadOutcome
{
    private EmailContentReadOutcome(
        StoredEmailId storedEmailId,
        ReadEmailContent? content,
        EmailContentReadFailure? failure)
    {
        this.StoredEmailId = storedEmailId;
        this.Content = content;
        this.Failure = failure;
    }

    /// <summary>Gets the email this outcome answers for.</summary>
    public StoredEmailId StoredEmailId { get; }

    /// <summary>Gets the email as a reader receives it, or <see langword="null" /> when it could not be served.</summary>
    public ReadEmailContent? Content { get; }

    /// <summary>Gets why the email could not be served, or <see langword="null" /> when it was.</summary>
    public EmailContentReadFailure? Failure { get; }

    /// <summary>Reports an email that was read.</summary>
    /// <param name="content">The email as a reader receives it.</param>
    /// <returns>The outcome carrying it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content" /> is <see langword="null" />.</exception>
    public static EmailContentReadOutcome Read(ReadEmailContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new EmailContentReadOutcome(content.StoredEmailId, content, failure: null);
    }

    /// <summary>Reports an email the local mailbox copy holds no row for.</summary>
    /// <param name="storedEmailId">The email the read named.</param>
    /// <returns>The outcome carrying the refusal.</returns>
    public static EmailContentReadOutcome NotFound(StoredEmailId storedEmailId) => new(
        storedEmailId,
        content: null,
        EmailContentReadFailure.NotFound(storedEmailId));

    /// <summary>Reports an email whose stored content is missing, damaged, or unreadable.</summary>
    /// <param name="storedEmailId">The email the read named.</param>
    /// <param name="defect">What was found wrong with its stored content.</param>
    /// <returns>The outcome carrying the refusal.</returns>
    /// <remarks>A repair request is recorded before this outcome is produced, so the finding is durable whatever the caller does with it.</remarks>
    public static EmailContentReadOutcome ContentUnavailable(
        StoredEmailId storedEmailId,
        EmailContentDefect defect) => new(
        storedEmailId,
        content: null,
        EmailContentReadFailure.ContentUnavailable(storedEmailId, defect));
}
