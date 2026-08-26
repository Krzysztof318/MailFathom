// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>What one rule concluded about one email on one pass, kept so the decision can be explained afterwards.</summary>
/// <remarks>
/// <para>
/// Every column is MailFathom's own name for something or a number: a configured rule name, a derived revision, a
/// bounded outcome, a fact's declared name, two instants, and a duration. <strong>No fact value is stored</strong>, which
/// is the whole design of this table — a resolved fact is a sender address, a subject, or a span of extracted text, and
/// keeping one would make this a second copy of the mailbox under a retention nobody wrote for mail.
/// </para>
/// <para>
/// The email is a foreign key with a cascade, which is what makes the history inherit the deletion obligations of the
/// mail it describes: a message erased anywhere in this system takes the explanations naming it with it, through the
/// email's own deletion path rather than through a rule somebody has to remember.
/// </para>
/// <para>
/// It is append-only. Nothing amends an execution, so the row carries no concurrency token: there is no second writer
/// for one to protect against.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailRuleExecutionEntity
{
    /// <summary>The greatest length a rule set revision has, which is the derived identity's own fixed width.</summary>
    internal const int RevisionLength = 12;

    /// <summary>The greatest length a rule name has, which the rule section already enforces on what it declares.</summary>
    internal const int MaximumRuleNameLength = 64;

    /// <summary>The greatest length a stored member name has, whichever of the bounded outcomes it belongs to.</summary>
    internal const int MaximumOutcomeLength = 64;

    /// <summary>The greatest length a configured folder alias has.</summary>
    internal const int MaximumAliasLength = 128;

    public required Guid Id { get; set; }

    public required string MailboxAccountId { get; set; }

    /// <summary>Gets or sets the owner whose account the rule was evaluated over.</summary>
    public required Guid OwnerId { get; set; }

    public Guid StoredEmailId { get; set; }

    public required string RuleName { get; set; }

    public required string Revision { get; set; }

    /// <summary>Gets or sets which of the pass's two walks reached the email, held as its own name.</summary>
    public required string Trigger { get; set; }

    /// <summary>Gets or sets what the condition concluded, held as its own name.</summary>
    /// <remarks>
    /// The bounded outcomes here are held as their own names rather than as converted enums, which is what the answering
    /// record does and for the same reason: a converted enum fails materialization on a name it declares no member for,
    /// and this record is read a page at a time, so a value a later build wrote would fail the page holding it and every
    /// page after it.
    /// </remarks>
    public required string Outcome { get; set; }

    /// <summary>Gets or sets why the condition produced no answer, absent for every execution that produced one.</summary>
    public string? ConditionFailure { get; set; }

    /// <summary>Gets or sets the declared names of the facts the condition read, in the order it first read each.</summary>
    /// <remarks>
    /// The names and never the values. An array rather than a table of its own because a fact name is not something
    /// anything else in this schema refers to, and the count is bounded by the fact surface rather than by the mail.
    /// </remarks>
    public required string[] ReadFacts { get; set; }

    public DateTimeOffset EvaluatedAt { get; set; }

    /// <summary>Gets or sets how long the condition took to answer, including resolving the facts it read.</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>Gets the changes the rule declared and what became of each.</summary>
    public IList<MailRuleExecutedActionEntity> Actions { get; } = [];
}
