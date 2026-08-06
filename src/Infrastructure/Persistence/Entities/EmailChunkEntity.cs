// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One retrievable passage of a stored email, and what identifies the rules it was cut to.</summary>
/// <remarks>
/// <para>
/// A row of its own rather than a column on the email, because a message yields many passages and each of them is what
/// a vector will hang on. The chunk is the unit retrieval cites, so it carries the span it came from and nothing that
/// the message it belongs to already answers: the account, the folder, the sender, the recipients, the date, and the
/// subject are reached through <see cref="StoredEmail" /> instead of copied, which keeps this table from becoming a
/// second searchable copy of who somebody corresponds with.
/// </para>
/// <para>
/// The text is mail content and inherits the source message's classification, retention, export, and erasure
/// obligations whole. The cascade from the stored email is what keeps a deletion of the message a deletion of every
/// passage derived from it, and it is the path a later vector row inherits in turn.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailChunkEntity
{
    public Guid Id { get; set; }

    public Guid StoredEmailId { get; set; }

    public required StoredEmailEntity StoredEmail { get; set; }

    /// <summary>Gets or sets the chunk's position in its message, counted from zero in reading order.</summary>
    public int Ordinal { get; set; }

    /// <summary>Gets or sets where the passage begins in the extracted text it was cut from.</summary>
    public int StartOffset { get; set; }

    /// <summary>Gets or sets the passage itself.</summary>
    /// <remarks>
    /// Deliberately unbounded, unlike every column that holds something a message wrote. Its length is decided by
    /// <see cref="EmailChunkingRules.TargetCharacterCount" /> rather than by a sender, and extraction has already
    /// bounded the text all the chunks of one message are cut from; a column bound would add nothing except a write
    /// failure the first time those rules are tuned upwards.
    /// </remarks>
    public required string Text { get; set; }

    /// <summary>Gets or sets what identifies this passage under the rules that produced it.</summary>
    /// <remarks>
    /// Re-chunking compares this against what the chunker just derived, so an unchanged message writes nothing at all
    /// rather than replacing identical rows and orphaning whatever hangs on them.
    /// </remarks>
    public required string ContentHash { get; set; }

    /// <summary>Gets or sets the version of the boundary rules that produced this passage.</summary>
    /// <remarks>
    /// Nothing reads it to decide correctness — <see cref="ContentHash" /> already covers the rules, so a change to
    /// them is a changed hash whatever this column says. It is here so a backfill can be asked how much of a mailbox is
    /// still cut to the previous rules, which is the one question a hash cannot answer.
    /// </remarks>
    public int RuleSetVersion { get; set; }

    /// <summary>Gets or sets whether the text this passage came from was inferred from markup rather than read from a plain-text part.</summary>
    /// <remarks>
    /// Carried forward rather than re-derived, so a later ranking change can weigh a lossy reading differently without
    /// walking every message's extraction again.
    /// </remarks>
    public bool IsDerivedFromLossyHtml { get; set; }

    /// <summary>Gets or sets when this passage was cut, which tells a re-cut from an original one apart.</summary>
    public DateTimeOffset DerivedAt { get; set; }
}
