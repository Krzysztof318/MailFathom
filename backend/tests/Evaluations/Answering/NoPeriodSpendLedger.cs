// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Chat;
using MailFathom.Application.Retrieval.AskMail;

namespace MailFathom.Evaluations.Answering;

/// <summary>The period's spend ledger, which a scenario has no period for: every run is admitted and nothing is carried over.</summary>
/// <remarks>What one run consumed is still counted, by the run's own ledger, and that is what a scenario reads.</remarks>
internal sealed class NoPeriodSpendLedger : IMailAnsweringSpendLedger
{
    /// <inheritdoc />
    public Task<bool> TryAdmitRunAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    /// <inheritdoc />
    public Task RecordSpendAsync(ChatTokenUsage usage, CancellationToken cancellationToken) => Task.CompletedTask;
}
