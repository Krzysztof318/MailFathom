// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Discovery.Citations;

/// <summary>What following one citation produced.</summary>
/// <remarks>
/// <para>
/// The factories are what guarantee the shape: a private source carries nothing but the identity the caller named, a
/// resolution carries the message it belongs to, and a place inside that message is carried by at most one of
/// <see cref="Fragment" /> and <see cref="Attachment" />. A citation that named a message as such resolves with neither,
/// which is the whole of what it pointed at.
/// </para>
/// <para>
/// The message survives an unresolvable place deliberately, and that is what makes a citation outlive a re-cut of the
/// mail it points at: the passage a run cited is derived data that a later chunking pass may replace, while the message
/// it was cut from is the same message, so a reader is still taken to the correspondence rather than told the fact has
/// no source. The one unresolvable citation carrying no message is one whose stored copy is damaged, which is the case
/// where there was no reading to carry.
/// </para>
/// <para>
/// Everything reachable from a resolution except the identity and the outcome is mail content, and nothing here is
/// logged, traced, or exported.
/// </para>
/// </remarks>
public sealed record ResolvedCitation
{
    private ResolvedCitation(
        StoredEmailId storedEmailId,
        CitationResolutionOutcome outcome,
        CitedMessage? message,
        CitedFragment? fragment,
        CitedAttachment? attachment)
    {
        this.StoredEmailId = storedEmailId;
        this.Outcome = outcome;
        this.Message = message;
        this.Fragment = fragment;
        this.Attachment = attachment;
    }

    /// <summary>Gets the message the citation named, which is carried whatever became of it.</summary>
    public StoredEmailId StoredEmailId { get; }

    /// <summary>Gets what became of the citation.</summary>
    public CitationResolutionOutcome Outcome { get; }

    /// <summary>Gets the message the citation belongs to, or <see langword="null" /> for a source the caller may not read.</summary>
    public CitedMessage? Message { get; }

    /// <summary>Gets the passage the citation points at, or <see langword="null" /> where it points at no passage or that passage is gone.</summary>
    public CitedFragment? Fragment { get; }

    /// <summary>Gets the file the citation points at, or <see langword="null" /> where it points at no file or that file is gone.</summary>
    public CitedAttachment? Attachment { get; }

    /// <summary>Reports a citation followed to the message as such.</summary>
    /// <param name="message">The message the citation names.</param>
    /// <returns>The resolution.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message" /> is <see langword="null" />.</exception>
    public static ResolvedCitation Resolved(CitedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new ResolvedCitation(
            message.StoredEmailId,
            CitationResolutionOutcome.Resolved,
            message,
            fragment: null,
            attachment: null);
    }

    /// <summary>Reports a citation followed to one passage of a message.</summary>
    /// <param name="message">The message the passage belongs to.</param>
    /// <param name="fragment">The passage the fact was taken from.</param>
    /// <returns>The resolution.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public static ResolvedCitation Resolved(CitedMessage message, CitedFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(fragment);

        return new ResolvedCitation(
            message.StoredEmailId,
            CitationResolutionOutcome.Resolved,
            message,
            fragment,
            attachment: null);
    }

    /// <summary>Reports a citation followed to one file a message carries.</summary>
    /// <param name="message">The message carrying the file.</param>
    /// <param name="attachment">The file the fact was taken from.</param>
    /// <returns>The resolution.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public static ResolvedCitation Resolved(CitedMessage message, CitedAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(attachment);

        return new ResolvedCitation(
            message.StoredEmailId,
            CitationResolutionOutcome.Resolved,
            message,
            fragment: null,
            attachment);
    }

    /// <summary>Reports a message the caller may read whose cited place is no longer there.</summary>
    /// <param name="message">The message the citation belongs to.</param>
    /// <returns>The resolution.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Reported rather than resolved to the nearest passage or to the first file, because a citation that quietly landed
    /// somewhere else would be evidence for a fact it was never drawn from.
    /// </remarks>
    public static ResolvedCitation Unresolvable(CitedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new ResolvedCitation(
            message.StoredEmailId,
            CitationResolutionOutcome.Unresolvable,
            message,
            fragment: null,
            attachment: null);
    }

    /// <summary>Reports a message the caller may read whose stored copy could not be read at all.</summary>
    /// <param name="storedEmailId">The message the citation named.</param>
    /// <returns>The resolution.</returns>
    /// <remarks>
    /// Distinct from a private source because the caller is entitled to this message and the read failed on this
    /// deployment's own storage, which the read has already recorded a repair request for. It carries no message,
    /// there being no reading of one to carry.
    /// </remarks>
    public static ResolvedCitation Unresolvable(StoredEmailId storedEmailId) => new(
        storedEmailId,
        CitationResolutionOutcome.Unresolvable,
        message: null,
        fragment: null,
        attachment: null);

    /// <summary>Reports a source this caller may not read.</summary>
    /// <param name="storedEmailId">The message the citation named.</param>
    /// <returns>The resolution.</returns>
    /// <remarks>
    /// It carries nothing but the identity the caller already held. Mail belonging to somebody else and mail this
    /// deployment does not hold are answered identically and deliberately: telling the two apart would need a read
    /// outside the caller's scope, and the answer to it would say whether somebody else's message exists.
    /// </remarks>
    public static ResolvedCitation PrivateSource(StoredEmailId storedEmailId) => new(
        storedEmailId,
        CitationResolutionOutcome.PrivateSource,
        message: null,
        fragment: null,
        attachment: null);
}
