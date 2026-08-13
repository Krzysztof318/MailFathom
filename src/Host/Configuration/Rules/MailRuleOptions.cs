// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Application.Rules;

namespace MailFathom.Host.Configuration.Rules;

/// <summary>One rule as an operator declares it: what it is called, what it matches, and what a match does to the pass.</summary>
/// <remarks>
/// The order rules appear in the configuration is the order they are evaluated in, so nothing here declares a position.
/// A rule that should run before another is moved above it in the file, which is the one place that ordering can be
/// read without cross-referencing anything.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class MailRuleOptions
{
    /// <summary>Gets or sets the name the rule is reported under.</summary>
    /// <remarks>
    /// Restricted to letters, digits, and the three separators, because the name is what a log line and a run record
    /// name a rule by. Everything else about a rule may carry an address the operator typed; the name is the one part
    /// this section can promise carries no such thing, and that promise is only worth having if it is enforced.
    /// </remarks>
    [Required]
    [StringLength(64, MinimumLength = 1)]
    [RegularExpression("^[A-Za-z0-9][A-Za-z0-9 ._-]*$")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the identifiers of the accounts this rule applies to.</summary>
    /// <remarks>
    /// One rule reaches one or more accounts, named as they are in <c>MailSynchronization:Accounts</c>. Declaring none
    /// is how a rule is written for every account, which is what a single-account deployment writes and what a rule
    /// about a sender rather than about a mailbox usually wants. Every identifier here has to name an account the
    /// deployment declares, because a rule scoped to a mistyped account would otherwise reach no mail at all and say
    /// nothing about why.
    /// </remarks>
    public IList<string> Accounts { get; set; } = [];

    /// <summary>Gets or sets the expression deciding whether an email matches this rule.</summary>
    [Required]
    public string Condition { get; set; } = string.Empty;

    /// <summary>Gets or sets what a match does to the matching email.</summary>
    /// <remarks>
    /// Optional, because a rule that changes nothing is a rule that selects mail: paired with
    /// <see cref="StopWhenMatched" /> it is how mail is kept away from the rules below it. A combination naming two
    /// fates for one occurrence is refused when the configuration is read rather than resolved while mail is being
    /// processed.
    /// </remarks>
    public MailRuleActionOptions Actions { get; set; } = new();

    /// <summary>Gets or sets whether a match ends the pass rather than continuing to the rules below this one.</summary>
    public bool StopWhenMatched { get; set; }

    /// <summary>Gets or sets whether the rule takes part in a pass at all.</summary>
    /// <remarks>
    /// A rule switched off is left out of the bound set entirely, so it costs nothing and changes the set's revision
    /// exactly as removing it would. It exists so that a rule can be taken out of service without the condition being
    /// deleted and rewritten from memory later.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the automatic triggers that may run this rule.</summary>
    /// <remarks>
    /// <para>
    /// A rule takes part in the occasions it names here and in no others, so leaving the key out and writing an empty
    /// list say the same thing: a rule nothing fires by itself. Such a rule stays in the bound set, is validated like
    /// every other, and a whole-mailbox run is what applies it. That is a different statement from
    /// <see cref="Enabled" />, which leaves a rule out of the set altogether and makes it unrunnable. A rule that
    /// should run over arriving mail writes <c>Arrival</c>; one that should walk the mailbox on its own writes
    /// <c>Schedule</c> and the <see cref="Schedule" /> beside it, and those two are the whole vocabulary.
    /// </para>
    /// <para>
    /// The elements are text rather than the trigger itself, for the reason the actions are one key each: the binder
    /// drops an element it cannot convert, so a mistyped name in a bound list of triggers would arrive as a shorter
    /// list — and a list whose only entry was mistyped would arrive as the empty one, silently turning an automatic
    /// rule into a manual one. Read as text, every name reaches validation and an unreadable one is refused there.
    /// </para>
    /// </remarks>
    public IList<string> Triggers { get; set; } = [];

    /// <summary>Gets or sets the occasions a scheduled walk of this rule happens on.</summary>
    /// <remarks>
    /// <para>
    /// Written as <c>Every &lt;hh:mm:ss&gt;</c> or as <c>Daily at &lt;HH:mm&gt;</c> with an optional IANA time zone —
    /// <c>Daily at 03:30 Europe/Warsaw</c>. <strong>A time with no zone is UTC</strong>, which is worth reading twice: a
    /// housekeeping rule an owner believes runs at night is the one place that answer is noticed.
    /// </para>
    /// <para>
    /// Required by and only by the <c>Schedule</c> trigger. A schedule without the trigger names occasions nothing acts
    /// on, and the trigger without a schedule is a rule that could never fire, so both are refused when the
    /// configuration is read rather than resolved into whichever of them was probably meant.
    /// </para>
    /// </remarks>
    public string? Schedule { get; set; }

    /// <summary>Reads the declared triggers, leaving out a name this system cannot read.</summary>
    /// <returns>The triggers, empty for a rule only a requested walk runs.</returns>
    /// <remarks>
    /// A name this system cannot read is left out rather than thrown over, because it is reported by validation against
    /// the key an operator edits and reading it here would raise instead. <see cref="MailRuleSetMapper" /> refuses a set
    /// that reaches it with one, so the dropped name cannot become a rule nothing runs.
    /// </remarks>
    /// <summary>Reads the declared schedule, or nothing where the rule declares none or wrote one this system cannot read.</summary>
    /// <returns>The occasions a scheduled walk happens on, or <see langword="null" />.</returns>
    /// <remarks>
    /// An unreadable schedule is left out rather than raised, for the reason an unreadable trigger name is: it is
    /// reported by validation against the key an operator edits, and <see cref="MailRuleSetMapper" /> refuses a set that
    /// reaches it with one.
    /// </remarks>
    internal JobRecurrence? ToSchedule() =>
        JobRecurrence.TryParse(this.Schedule, out var recurrence, out _) ? recurrence : null;

    internal IReadOnlyList<MailRuleTrigger> ToTriggers() =>
    [
        .. this.Triggers
            .Select(name => MailRuleTrigger.TryParseName(name, out var trigger) ? trigger : default)
            .Where(trigger => trigger.IsSpecified),
    ];
}
