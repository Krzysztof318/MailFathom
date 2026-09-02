// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Rules.Evaluation;

/// <summary>Reads the recurring dispatches the declared rules ask for, one per scheduled rule and account it reaches.</summary>
/// <remarks>
/// <para>
/// A schedule per rule <em>and</em> account rather than per rule, because a rule reaching three mailboxes is three walks
/// and each has to be able to be under way, missed, or caught up on independently of the other two. The identity is
/// composed of both for that reason, and out of nothing else: it is a key an operator reads, so it is made of the names
/// they wrote.
/// </para>
/// <para>
/// What a scheduled walk then evaluates is every rule declaring the schedule trigger for that account, not only the rule
/// whose occasion started it. That is one walk of a mailbox per occasion instead of one per rule, which is the whole of
/// the saving: a mailbox is read once and every rule that opted into schedules is applied to what was read. A rule
/// declaring the shorter interval therefore brings the others round with it, which is a deliberate trade and is what
/// <c>Triggers</c> already means everywhere else — the trigger decides which rules a walk reaches, and the schedule
/// decides when a walk starts.
/// </para>
/// <para>
/// Read from the rule set in force rather than held, so an edit that adds, moves, or removes a schedule reaches the next
/// pass. A schedule an edit removed simply stops being declared; the row recording what it last did stays behind, which
/// is what makes putting the rule back a resumption rather than a fresh start.
/// </para>
/// </remarks>
public sealed class MailRuleScheduleSource : IScheduledJobSource
{
    /// <summary>The word every schedule this source declares is prefixed with, so a key says what declared it.</summary>
    private const string IdentityPrefix = "mail-rules";

    private readonly IMailRuleSetSource ruleSetSource;
    private readonly IDeploymentMailAccountCatalog accounts;

    /// <summary>Initializes the source over the rules in force and the accounts they may reach.</summary>
    /// <param name="ruleSetSource">Hands out the rule set the schedules are read from.</param>
    /// <param name="accounts">Names the accounts this deployment serves, which is what an unscoped rule reaches.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public MailRuleScheduleSource(IMailRuleSetSource ruleSetSource, IDeploymentMailAccountCatalog accounts)
    {
        ArgumentNullException.ThrowIfNull(ruleSetSource);
        ArgumentNullException.ThrowIfNull(accounts);

        this.ruleSetSource = ruleSetSource;
        this.accounts = accounts;
    }

    /// <inheritdoc />
    /// <remarks>The rules and the accounts are both already in memory, so this source waits for nothing and answers with a completed task.</remarks>
    public Task<IReadOnlyList<ScheduledJob>> ReadSchedulesAsync(CancellationToken cancellationToken)
    {
        var servedAccounts = this.accounts.ServedAccounts;

        IReadOnlyList<ScheduledJob> declared =
            [.. this.ruleSetSource.Current.Rules.SelectMany(rule => SchedulesOf(rule, servedAccounts))];

        return Task.FromResult(declared);
    }

    /// <summary>Reads the schedules one rule declares, which is one per account it reaches and none where it declares no schedule.</summary>
    private static IEnumerable<ScheduledJob> SchedulesOf(
        MailRule rule,
        IReadOnlyList<ServedMailAccount> servedAccounts) => rule.Schedule is { } recurrence
        ? servedAccounts
            .Where(account => rule.AppliesTo(account.Id.Value))
            .Select(account => Declare(rule.Name, recurrence, account.Identity))
        : [];

    /// <summary>Declares one rule's schedule for one account, as the repeated work a dispatch reads.</summary>
    /// <remarks>
    /// The payload names the owner beside the identifier, because the run it starts writes rows about that account. The
    /// schedule's own identity is still composed from the identifier alone: making every identity composed as text say
    /// whose account it names is a later step of ADR 0014's delivery order, and taking it here would move the durable
    /// state a deployment already keeps under those strings.
    /// </remarks>
    private static ScheduledJob Declare(string ruleName, JobRecurrence recurrence, MailAccountIdentity account) => new(
        JobScheduleId.Create($"{IdentityPrefix}:{account.Id.Value}:{ruleName}"),
        RunScheduledMailRulesJobPayload.For(account),
        recurrence,
        account);
}
