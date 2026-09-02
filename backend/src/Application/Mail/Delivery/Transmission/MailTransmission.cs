// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Transmission;

/// <summary>Reports what a submission server said about the message as a whole.</summary>
/// <remarks>
/// <para>
/// It is returned only where the server answered, and a server that answered has settled the question: an acceptance
/// says the message was taken and either refusal says it was not, whatever had already been accepted about the
/// envelope. A submission that produced no answer — a connection that dropped, a budget that expired, a host that
/// stopped — raises instead, and what the recipients received is then read off the envelope ledger the caller supplied.
/// </para>
/// <para>
/// What each address settled at is on that ledger rather than repeated here, so a caller reading the two cannot find
/// them disagreeing.
/// </para>
/// </remarks>
/// <param name="Outcome">How the submission ended for the message.</param>
/// <param name="ReplyCode">
/// The reply code the server answered the message itself with, or <see langword="null" /> when it stated none this
/// attempt could read. A refusal always carries one. An acceptance need not: the mail library publishes the free-form
/// text of the final reply and not its three digits, and inventing the code a successful submission "must" have
/// answered with would put a number nobody read onto a record an operator trusts.
/// </param>
public sealed record MailTransmission(MailTransmissionOutcome Outcome, int? ReplyCode);
