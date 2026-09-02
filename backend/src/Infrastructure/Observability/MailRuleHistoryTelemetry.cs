// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Reports rows of the rule history this build served a page without.</summary>
/// <remarks>
/// <para>
/// It has one thing to say, and that narrowness is what it is for. A row naming a fact, an outcome, or a way an action
/// can fail that this build does not declare is left out of the page rather than failing it, because the history is
/// paginated by position and one refused row would otherwise take every page after it with it. Leaving it out is only
/// defensible while it is visible — a history that quietly omits executions is worse than one that says it did.
/// </para>
/// <para>
/// Nothing recorded here is mail. An account identifier and a count of rows are MailFathom's own names and numbers.
/// </para>
/// </remarks>
public sealed partial class MailRuleHistoryTelemetry
{
    private readonly ILogger<MailRuleHistoryTelemetry> logger;

    /// <summary>Initializes the channel an unreadable row is reported through.</summary>
    /// <param name="logger">Records the rows that were left out.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger" /> is <see langword="null" />.</exception>
    public MailRuleHistoryTelemetry(ILogger<MailRuleHistoryTelemetry> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        this.logger = logger;
    }

    /// <summary>Says that a page left out executions this build cannot interpret.</summary>
    /// <param name="accountId">The account whose history was read.</param>
    /// <param name="unreadableCount">How many rows of the page were left out.</param>
    /// <remarks>
    /// The rows are still in the history and a later build reads them; what is reported is that this build's answer is
    /// short of them.
    /// </remarks>
    internal void RecordUnreadableExecutions(MailAccountId accountId, int unreadableCount) =>
        this.LogExecutionsUnreadable(accountId.Value, unreadableCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Account {AccountId} holds {UnreadableCount} rule executions this build cannot interpret, which were left out of the page it served; a build that declares the values they name reads them.")]
    private partial void LogExecutionsUnreadable(string accountId, int unreadableCount);
}
