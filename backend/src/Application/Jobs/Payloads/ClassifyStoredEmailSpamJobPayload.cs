// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Jobs.Payloads;

/// <summary>Points one job at a single stored email, and at nothing inside the message.</summary>
/// <remarks>
/// <para>
/// Every property is one of MailFathom's own identifiers: the user the mailbox belongs to, the account within them, and
/// the email's own stored identity. A handler therefore resolves what it needs from committed local state rather than
/// from anything the enqueuer copied, and a subject, an address, a body, and extracted text are all absent by
/// construction: there is no property to put one in.
/// </para>
/// <para>
/// The email is named by its stored identity rather than by where a mail server holds it, because that identity is what
/// the email is for its whole life. A UID is renumbered by a UIDVALIDITY change and cleared once no server holds the
/// message, and a job naming one would then find nothing to classify although the mail is still here.
/// </para>
/// <para>
/// The properties are primitives rather than the domain value objects they came from, because this record is the stored
/// document: it is serialized into one <c>jsonb</c> column and read by an operator looking at a queue.
/// </para>
/// </remarks>
public sealed record ClassifyStoredEmailSpamJobPayload : IJobPayload
{
    /// <summary>Gets the user whose account the email belongs to.</summary>
    /// <remarks>
    /// Named beside the identifier, because an identifier names one account within its user and the rows this work
    /// writes are about that account. The user is generated and names nobody outside this deployment, so carrying it
    /// discloses nothing an operator reading a queued job may not see.
    /// </remarks>
    public required Guid UserId { get; init; }

    /// <summary>Gets the account whose mailbox the email belongs to, within that user.</summary>
    public required string AccountId { get; init; }

    /// <summary>Gets the stored identity of the email to classify.</summary>
    public required Guid StoredEmailId { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public JobType JobType => JobType.ClassifyStoredEmailSpam;

    /// <summary>Describes one stored email as the document a job carries.</summary>
    /// <param name="account">The account the email belongs to, as the run that stored it resolved.</param>
    /// <param name="email">The email's stored identity.</param>
    /// <returns>The payload naming that email.</returns>
    public static ClassifyStoredEmailSpamJobPayload For(MailAccountIdentity account, StoredEmailId email) => new()
    {
        UserId = account.User.Value,
        AccountId = account.Id.Value,
        StoredEmailId = email.Value,
    };

    /// <summary>Rebuilds the account identity this payload names.</summary>
    /// <returns>The account identity.</returns>
    /// <exception cref="ArgumentException">Thrown when the stored values no longer name a valid account identity.</exception>
    public MailAccountIdentity ToAccountIdentity() =>
        MailAccountIdentity.Create(MailUserId.Create(this.UserId), MailAccountId.Create(this.AccountId));

    /// <summary>Rebuilds the stored identity this payload names.</summary>
    /// <returns>The email's stored identity.</returns>
    /// <exception cref="ArgumentException">Thrown when the stored value is the empty identifier.</exception>
    public StoredEmailId ToStoredEmailId() => Domain.Emails.StoredEmailId.Create(this.StoredEmailId);
}
