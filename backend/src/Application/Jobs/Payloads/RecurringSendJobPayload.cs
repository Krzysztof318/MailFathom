// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery.Scheduling;

namespace MailFathom.Application.Jobs.Payloads;

/// <summary>Points one job at a declaration that a message is sent again on every occasion its schedule names.</summary>
/// <remarks>
/// <para>
/// It names the declaration and not the occasion, because a recurring dispatch repeats one piece of work and the
/// occasion is what the schedule decides. Which occasion a run is for is read from the schedule at the instant the work
/// happens, so the document stays the same for the life of the declaration and two instances reaching one occasion
/// compose one message rather than two.
/// </para>
/// <para>
/// Every property is one of MailFathom's own identifiers. The message this declaration repeats is in the content store with
/// every other piece of RFC 822 this system holds, and nothing about it — not a recipient, not a subject — is in the
/// document a queued job is read from.
/// </para>
/// </remarks>
public sealed record RecurringSendJobPayload : IJobPayload
{
    /// <summary>Gets the owner whose account every occurrence is sent as.</summary>
    public required Guid OwnerId { get; init; }

    /// <summary>Gets the account every occurrence is submitted through and sent as, within that owner.</summary>
    public required string AccountId { get; init; }

    /// <summary>Gets the declaration whose occasion has come round.</summary>
    /// <remarks>Named for the declaration rather than for the type that wraps it, because a property carrying the type's own name would hide it inside this record and leave the identity rebuilt through a qualified name.</remarks>
    public required Guid DeclarationId { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public JobType JobType => JobType.SendRecurringOccurrence;

    /// <summary>Describes one recurring send as the document a job carries.</summary>
    /// <param name="account">The account every occurrence is sent as, named by its owner and its identifier together.</param>
    /// <param name="recurringSendId">The declaration the occasion belongs to.</param>
    /// <returns>The payload naming that declaration.</returns>
    public static RecurringSendJobPayload For(MailAccountIdentity account, RecurringSendId recurringSendId) => new()
    {
        OwnerId = account.Owner.Value,
        AccountId = account.Id.Value,
        DeclarationId = recurringSendId.Value,
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

    /// <summary>Rebuilds the declaration identity this payload names.</summary>
    /// <returns>The declaration identity.</returns>
    /// <exception cref="ArgumentException">Thrown when the stored value is empty.</exception>
    public RecurringSendId ToRecurringSendId() => RecurringSendId.Create(this.DeclarationId);
}
