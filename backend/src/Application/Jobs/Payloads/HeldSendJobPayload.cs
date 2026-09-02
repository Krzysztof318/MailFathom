// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Jobs.Payloads;

/// <summary>Points one job at a message already written down and waiting for the time it was written to leave at.</summary>
/// <remarks>
/// <para>
/// Every property is one of MailFathom's own identifiers: the owner the mailbox belongs to, the deployment's
/// configured name for that mailbox within them, and the surrogate its own outgoing record carries. Nothing about the
/// message is here — no recipient, no subject, and nothing that could become one — because the message itself is
/// already durable and this job only says that its moment has come.
/// </para>
/// <para>
/// The record is named as well as the account, although what the work does is reach the account's outbox. It is what
/// makes a queued job readable — an operator asking why a message has not left reads which message this job is for —
/// and it is what lets the work stand down for a send that was cancelled while it was held.
/// </para>
/// </remarks>
public sealed record HeldSendJobPayload : IJobPayload
{
    /// <summary>Gets the owner whose account holds the message.</summary>
    public required Guid OwnerId { get; init; }

    /// <summary>Gets the account whose outbox holds the message, within that owner.</summary>
    public required string AccountId { get; init; }

    /// <summary>Gets the outgoing record the message was written down as.</summary>
    /// <remarks>Named for the record rather than for the type that wraps it, because a property carrying the type's own name would hide it inside this record and leave the identity rebuilt through a qualified name.</remarks>
    public required Guid OutgoingRecordId { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public JobType JobType => JobType.DispatchHeldSend;

    /// <summary>Describes one held send as the document a job carries.</summary>
    /// <param name="account">The account whose outbox holds the message, named by its owner and its identifier together.</param>
    /// <param name="outgoingEmailId">The record the message was written down as.</param>
    /// <returns>The payload naming that held send.</returns>
    public static HeldSendJobPayload For(MailAccountIdentity account, OutgoingEmailId outgoingEmailId) => new()
    {
        OwnerId = account.Owner.Value,
        AccountId = account.Id.Value,
        OutgoingRecordId = outgoingEmailId.Value,
    };

    /// <summary>Rebuilds the account identity this payload names.</summary>
    /// <returns>The account identity.</returns>
    /// <exception cref="ArgumentException">Thrown when the stored values no longer name a valid account identity.</exception>
    /// <remarks>
    /// The owner is a required property, so a document that carries none is refused by the deserializer before
    /// this is reached rather than resolving to an owner nobody named. A document the previous release wrote is
    /// not that case: the migration that put the owner on the queue row writes it into the document beside it, so
    /// what remains here is a value that is present and does not name an account — which this refuses for the
    /// reason every payload record refuses a component that no longer validates.
    /// </remarks>
    public MailAccountIdentity ToAccountIdentity() =>
        MailAccountIdentity.Create(MailOwnerId.Create(this.OwnerId), MailAccountId.Create(this.AccountId));

    /// <summary>Rebuilds the outgoing record identity this payload names.</summary>
    /// <returns>The record identity.</returns>
    /// <exception cref="ArgumentException">Thrown when the stored value is empty.</exception>
    public OutgoingEmailId ToOutgoingEmailId() => OutgoingEmailId.Create(this.OutgoingRecordId);
}
