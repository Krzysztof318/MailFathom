// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Facts;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Rules.History;

/// <summary>What one rule concluded about one email on one pass, and what that conclusion asked the mailbox for.</summary>
/// <remarks>
/// <para>
/// The unit of the history is the pair of a rule and a message, because those are the two ways an operator arrives at
/// it: "why is this message here" and "what is this rule doing". A rule the pass never reached — one below a rule that
/// ended the pass — leaves no execution at all, which is what keeps "did not match" and "was never asked" apart.
/// </para>
/// <para>
/// <strong>The facts are recorded by name and never by value.</strong> A resolved fact is a sender address, a subject,
/// or a span of extracted text, so recording what it evaluated to is exactly the second copy of the mailbox this record
/// exists not to be. The names are sufficient because the revision is recorded beside them: the expression is
/// retrievable from the configuration revision the pass was bound to, so <c>senderDomain</c>, <c>hasAttachment</c>, and
/// a true result reconstruct the reasoning without reconstructing the mail.
/// </para>
/// <para>
/// Nothing here is amended once written. An execution states a reading that has already happened, so the record only
/// ever grows and shrinks — by the retention window it is held for, and by the erasure of the message it names.
/// </para>
/// </remarks>
public sealed record MailRuleExecution
{
    /// <summary>Gets what addresses this execution.</summary>
    public required MailRuleExecutionId Id { get; init; }

    /// <summary>Gets the account whose mail was evaluated.</summary>
    public required MailAccountIdentity Account { get; init; }

    /// <summary>Gets the local identity of the email the rule was evaluated against.</summary>
    public required StoredEmailId StoredEmailId { get; init; }

    /// <summary>Gets the name of the rule, which is MailFathom's own configured name for it.</summary>
    public required string RuleName { get; init; }

    /// <summary>Gets the rule set revision the pass was bound to, which is what the condition is retrievable from.</summary>
    public required MailRuleSetRevision Revision { get; init; }

    /// <summary>Gets which of the pass's two walks reached the email.</summary>
    public required MailRuleExecutionTrigger Trigger { get; init; }

    /// <summary>Gets what the condition concluded.</summary>
    public required MailRuleOutcome Outcome { get; init; }

    /// <summary>Gets why the condition produced no answer, which is present exactly when the outcome is <see cref="MailRuleOutcome.Failed" />.</summary>
    public MailRuleConditionFailure? ConditionFailure { get; init; }

    /// <summary>Gets the facts the condition read, by name, in the order the fact surface declares them.</summary>
    /// <remarks>
    /// Read rather than referenced. A condition short-circuits, so a fact its text names is not necessarily one the
    /// evaluation needed — and the difference is what tells an operator whether a rule is paying for a stored-content
    /// read on every message or only on the few that reach the clause naming it.
    /// </remarks>
    public required IReadOnlyList<MailRuleFact> ReadFacts { get; init; }

    /// <summary>Gets the changes the rule declared and what became of each, in the order the rule declares them.</summary>
    /// <remarks>Empty for a rule that did not match, and for a matching rule that changes nothing.</remarks>
    public required IReadOnlyList<MailRuleExecutedAction> Actions { get; init; }

    /// <summary>Gets the instant the email was evaluated at, which every age fact of the pass was measured against.</summary>
    public required DateTimeOffset EvaluatedAt { get; init; }

    /// <summary>Gets how long the condition took to answer, including resolving the facts it read.</summary>
    /// <remarks>
    /// The measurement the evaluation timeout is spent against, so a rule creeping toward its bound is visible before it
    /// starts being recorded as timed out rather than afterwards.
    /// </remarks>
    public required TimeSpan Duration { get; init; }
}
