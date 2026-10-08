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
    /// <remarks>
    /// The rules are already in memory and the accounts are read from the account records, once per reading of the
    /// schedules — and not at all where no rule declares a schedule, which is the ordinary deployment.
    /// </remarks>
    public async Task<IReadOnlyList<ScheduledJob>> ReadSchedulesAsync(CancellationToken cancellationToken)
    {
        var rules = this.ruleSetSource.Current.Rules;

        if (rules.All(rule => rule.Schedule is null))
        {
            return [];
        }

        var servedAccounts = await this.accounts.ReadServedAccountsAsync(cancellationToken);

        return [.. rules.SelectMany(rule => SchedulesOf(rule, servedAccounts))];
    }

    /// <summary>Reads the schedules one rule declares, which is one per account it reaches and none where it declares no schedule.</summary>
    private static IEnumerable<ScheduledJob> SchedulesOf(
        MailRule rule,
        IReadOnlyList<ServedMailAccount> servedAccounts) => rule.Schedule is { } recurrence
        ? servedAccounts
            .Where(account => rule.AppliesTo(account.Id.Value))
            .Select(account => Declare(rule.Name, recurrence, account.Id))
        : [];

    /// <summary>Declares one rule's schedule for one account, as the repeated work a dispatch reads.</summary>
    /// <remarks>
    /// The identity is composed from the account's generated identifier and the rule's name, and from nothing else. No
    /// user stands in it: a mailbox two people are assigned declares one schedule rather than one each, so the rule
    /// runs over that mail once however many people read it.
    /// </remarks>
    private static ScheduledJob Declare(string ruleName, JobRecurrence recurrence, MailAccountId account) => new(
        JobScheduleId.Create($"{IdentityPrefix}:{account.Value}:{ruleName}"),
        RunScheduledMailRulesJobPayload.For(account),
        recurrence,
        account);
}
