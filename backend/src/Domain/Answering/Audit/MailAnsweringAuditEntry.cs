// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Domain.Answering.Audit;

/// <summary>States what one answering run read from one account's mailbox, in the form that outlives the mail it read.</summary>
/// <remarks>
/// <para>
/// The artifact an answer is explained by afterwards. An answer produced by a model is not reproducible — asked twice,
/// the same question over the same mailbox can produce two different answers — so the only way to explain one later is
/// to have recorded what produced it. What a run cost, how long it took, and how it degraded are bounded facts that
/// belong on the span beside the request they happened in; which messages it read is the part that cannot, because a tag
/// per message would open a time series per person and because a span store is not MailFathom's to carry an obligation
/// in.
/// </para>
/// <para>
/// It holds no mail. The identifiers, this deployment's own configured names, two counts, and a bounded ending are the
/// whole of it: a record that stored the retrieved passages would create a second copy of the mailbox with its own
/// retention, access, export, and erasure obligations. It is still derived personal data — it says what a person's mail
/// was read for and when — which is why it is written only for an account whose operator turned the record on.
/// </para>
/// <para>
/// One entry per account in the run's scope rather than one per run, because enabling the record, the window it is kept
/// for, and erasing it are decisions one account's operator makes. An entry therefore names only the mail of the account
/// it belongs to, and a question asked across two accounts is two entries sharing a <see cref="RunId" />.
/// </para>
/// <para>
/// Unlike the mutation trail beside it, the emails an entry names are reached by their own deletion path. A mutation
/// entry survives the mail it describes because the act it records may have <em>been</em> that deletion; nothing of the
/// sort applies here, so an erased message is erased from the runs that read it and the entry is left saying that a run
/// happened and read something now gone.
/// </para>
/// </remarks>
public sealed record MailAnsweringAuditEntry
{
    /// <summary>Gets what addresses this entry.</summary>
    public required MailAnsweringAuditEntryId Id { get; init; }

    /// <summary>Gets the run this entry records, which the entries of the run's other accounts share.</summary>
    public required MailAnsweringRunId RunId { get; init; }

    /// <summary>Gets the account whose mailbox the run was allowed to read, named by its owner and its identifier.</summary>
    /// <remarks>
    /// The owner comes from the scope the run resolved, which settled whose accounts were reachable before anything was
    /// read, so the entry records whose mailbox was queried without asking the account table again. It travels as one
    /// value with the identifier because an identifier names an account within its owner and nowhere else.
    /// </remarks>
    public required MailAccountIdentity Account { get; init; }

    /// <summary>Gets the identifier half of <see cref="Account" />, which is the name an operator wrote.</summary>
    public MailAccountId AccountId => this.Account.Id;

    /// <summary>Gets the owner whose account the question was asked of.</summary>
    public MailOwnerId Owner => this.Account.Owner;

    /// <summary>Gets the emails of this account the run retrieved, in the order it first reached each, and which of them the answer named.</summary>
    /// <remarks>
    /// Empty for a run that read nothing of this account — a question answered without a lookup, one whose searches
    /// matched nothing here, and one that failed before retrieving. That is a recorded fact rather than a missing one:
    /// the question was asked of this mailbox and drew nothing out of it.
    /// </remarks>
    public required IReadOnlyList<MailAnsweringAuditedEmail> Emails { get; init; }

    /// <summary>Gets the alias of the chat endpoint the run was conducted through.</summary>
    /// <remarks>
    /// This deployment's own configured name for the model profile, never the model identifier the provider publishes
    /// and never the credential. It is what an operator joins the entry to the declaration that produced it, and
    /// answering the same question through a different profile is exactly the thing the record has to distinguish.
    /// </remarks>
    public required string ChatEndpointAlias { get; init; }

    /// <summary>Gets the version of the instruction the run was conducted under.</summary>
    /// <remarks>
    /// The policy half of "what produced this answer". The instruction states how retrieved mail is to be read, what may
    /// not be obeyed, and how claims are cited, so an answer written under one revision of it is not evidence about
    /// another. The text itself is not kept: it is a constant of the build rather than anything a run composes, so the
    /// version names it and the build carries it.
    /// </remarks>
    public required string InstructionsVersion { get; init; }

    /// <summary>Gets when the run began.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Gets when the run reached the ending this entry records.</summary>
    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>Gets how the run ended.</summary>
    public required MailAnsweringRunOutcome Outcome { get; init; }

    /// <summary>Gets the ways the run read less of the mailbox than an undegraded run of the same question would.</summary>
    public required MailAnsweringRunDegradation Degradation { get; init; }
}
