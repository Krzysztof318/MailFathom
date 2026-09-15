// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Synchronization.Drain;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Observability;

/// <summary>Publishes how far a held account's source has been emptied, and what the gate is keeping on it.</summary>
/// <remarks>
/// <para>
/// The counts are the only way an operator learns how far a switch has got, because what a client sees about a held
/// message is nothing at all: its presence on the source is not a property a person acts on. So this reports the work
/// rather than the mail — drained, held back and for which reason, and failed — and carries no subject, address,
/// folder path, or anything else derived from a message.
/// </para>
/// <para>
/// The account is a dimension because a deployment holds several and an operator switches them one at a time. The
/// counters are one replica's, like every instrument here: a dashboard sums them across replicas, and one account's
/// drain runs under one lease at a time so the sum is the account's.
/// </para>
/// </remarks>
public interface IMailboxDrainTelemetry
{
    /// <summary>Records what one pass over an account's source did.</summary>
    /// <param name="account">The account whose source was drained.</param>
    /// <param name="report">The counts the pass produced.</param>
    void Report(MailAccountId account, MailboxDrainReport report);
}
