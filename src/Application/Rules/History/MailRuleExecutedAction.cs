// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Actions;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Rules.History;

/// <summary>One change a matching rule declared, and what the pass did about it.</summary>
/// <param name="Position">Where the action sits in the order the rule declares its changes, counted from zero.</param>
/// <param name="Mutation">The change asked for, which is the same word a log line, a counter, and the mutation trail use.</param>
/// <param name="Outcome">What became of it.</param>
/// <param name="DestinationAlias">The folder the action named, and <see langword="null" /> for an action naming none.</param>
/// <param name="FailureReason">Why nothing was recorded, which is present exactly when the outcome is <see cref="MailRuleExecutedActionOutcome.Refused" />.</param>
/// <param name="MutationRecordId">The record carrying the request, which is present exactly when the outcome is <see cref="MailRuleExecutedActionOutcome.Requested" />.</param>
/// <remarks>
/// <para>
/// Every value here is MailFathom's own name for something: a mutation name, a configured folder alias, a bounded
/// reason, and two identifiers. Nothing is derived from the message, which is what lets an operator be told what a rule
/// did to a mailbox without the record becoming a second copy of it.
/// </para>
/// <para>
/// The record identifier is a pointer rather than a copy. What happened on the server — attempted, converged, given up
/// on — is the mutation's own trail, with its own retention, and restating any of it here would leave two answers to one
/// question that could disagree.
/// </para>
/// </remarks>
public sealed record MailRuleExecutedAction(
    int Position,
    MailboxMutation Mutation,
    MailRuleExecutedActionOutcome Outcome,
    MailFolderAlias? DestinationAlias = null,
    MailRuleActionFailureReason? FailureReason = null,
    MailboxMutationRecordId? MutationRecordId = null);
