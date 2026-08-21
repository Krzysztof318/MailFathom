// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam.Runs;

/// <summary>A whole-mailbox classification run somebody asked for, and how far the account's runs have carried it.</summary>
/// <remarks>
/// <para>
/// Durable because it has to survive the process. The run spans as many account runs as its batch budget needs, so a
/// restart in the middle of one must resume at the message nobody has reached rather than at the beginning of a mailbox
/// — and a request that arrived seconds before a shutdown must still be a request afterwards.
/// </para>
/// <para>
/// One outstanding run per account, which is what makes a second request an answer rather than a second walk over one
/// mailbox. There is no queue behind it: asking twice for the same thing is asking once, and the reply says the run is
/// already under way.
/// </para>
/// <para>
/// Every field is either MailFathom's own identity for something, a count, or an answer the operator gave when they
/// asked. Nothing derived from a message belongs in a record an operator reads to find out what their instance is doing,
/// which is why the verdicts appear here only as counts and are readable per message from the classification records
/// themselves.
/// </para>
/// </remarks>
public sealed record SpamClassificationRun
{
    /// <summary>Gets the account whose mail the run walks.</summary>
    public required MailAccountId AccountId { get; init; }

    /// <summary>Gets when the run was asked for.</summary>
    public required DateTimeOffset RequestedAt { get; init; }

    /// <summary>Gets what the operator asked the run to do, fixed when they asked.</summary>
    public required SpamClassificationRunTerms Terms { get; init; }

    /// <summary>Gets the settings the run is bound to, which are unspecified until the first pass picks the run up.</summary>
    /// <remarks>
    /// Bound when the run starts rather than when it is requested, because a request is answered on a thread that has no
    /// business deciding what the run will conclude: the settings may reload between the two, and the profile that
    /// matters is the one in force when the first message is actually scored.
    /// </remarks>
    public SpamClassificationProfile Profile { get; init; }

    /// <summary>Gets the identity of the last occurrence a batch committed, or <see langword="null" /> while it has committed none.</summary>
    public StoredEmailId? Position { get; init; }

    /// <summary>Gets how many occurrences the run recorded a verdict for.</summary>
    public int ClassifiedEmailCount { get; init; }

    /// <summary>Gets how many of the occurrences the run reached carry a spam verdict.</summary>
    /// <remarks>
    /// Counted over what the run reached rather than over what it scored, so a message it skipped because the verdict it
    /// already carried was reached under the same terms counts here too. The question the count answers is how much junk
    /// is in the mailbox, and a message's answer to that does not depend on which pass scored it.
    /// </remarks>
    public int SpamEmailCount { get; init; }

    /// <summary>Gets how many of the occurrences the run reached carry a verdict that concluded nothing either way.</summary>
    public int UndeterminedEmailCount { get; init; }

    /// <summary>Gets how many occurrences the run passed over because they were already decided under its profile.</summary>
    /// <remarks>
    /// A skipped occurrence is still acted on. What was skipped is the scoring, which is the expensive half and the half
    /// whose answer would not change; the verdict it already carries is what the run's posture is then applied to.
    /// </remarks>
    public int SkippedEmailCount { get; init; }

    /// <summary>Gets how many occurrences the run could reach no verdict about.</summary>
    /// <remarks>
    /// Mail whose local content is not stored, mail that left the mailbox between the walk reading it and the scoring
    /// reaching it, and mail in a folder the configured scope stopped covering while the run was outstanding. All three
    /// are answers rather than failures, and all three are reachable again by a later run without the message being
    /// fetched from the mail server for classification's sake.
    /// </remarks>
    public int UnclassifiableEmailCount { get; init; }

    /// <summary>Gets how many occurrences the run asked the mailbox to change, or would have asked for in a dry run.</summary>
    /// <remarks>
    /// One count for both postures, because the posture is on this record and the count means the same thing under each:
    /// this is the mail the switches reach. In a dry run it is the whole of what the operator is deciding whether to
    /// allow.
    /// </remarks>
    public int ActedEmailCount { get; init; }

    /// <summary>Gets when the run stopped being outstanding, or <see langword="null" /> while it still is.</summary>
    public DateTimeOffset? EndedAt { get; init; }

    /// <summary>Gets how the run ended, which is absent for exactly as long as <see cref="EndedAt" /> is.</summary>
    public SpamClassificationRunEnding? Ending { get; init; }

    /// <summary>Gets whether the run is still waiting to be carried further by an account run.</summary>
    public bool IsOutstanding => this.EndedAt is null;
}
