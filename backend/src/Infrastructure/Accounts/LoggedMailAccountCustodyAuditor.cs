// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts.Custody;
using MailFathom.Domain.Accounts;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Accounts;

/// <summary>Writes each custody decision to the deployment's log, which is where an administrative act is accounted for.</summary>
/// <remarks>
/// A refused switch is logged at the same level as an accepted one. An attempt to empty a mailbox is an event whether
/// or not it was allowed to proceed, and a refusal that left no trace would make the record of who tried to do what
/// depend on whether they got it right.
/// </remarks>
internal sealed partial class LoggedMailAccountCustodyAuditor(ILogger<LoggedMailAccountCustodyAuditor> logger)
    : IMailAccountCustodyAuditor
{
    /// <inheritdoc />
    public Task RecordAsync(MailAccountCustodyDecision decision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);

        this.LogCustodyDecided(
            decision.Account.Value,
            decision.RequestedBy,
            decision.From,
            decision.To,
            decision.WasAccepted,
            decision.DecidedAt);

        // One line per reason rather than one joined into the line above, so a refusal an operator is searching for is
        // a field they can filter on rather than text inside a sentence — and so composing the record costs nothing
        // for the accepted switch, which is the case with no reasons at all.
        foreach (var refusal in decision.Refusals)
        {
            this.LogCustodyRefused(decision.Account.Value, refusal);
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Custody of account {AccountId} was asked by {RequestedBy} to move from {FromCustody} to {ToCustody}; accepted: {WasAccepted}, at {DecidedAt}.")]
    private partial void LogCustodyDecided(
        string accountId,
        string requestedBy,
        MailAccountCustody fromCustody,
        MailAccountCustody toCustody,
        bool wasAccepted,
        DateTimeOffset decidedAt);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The custody switch of account {AccountId} was refused: {Refusal}.")]
    private partial void LogCustodyRefused(string accountId, MailAccountCustodySwitchRefusal refusal);
}
