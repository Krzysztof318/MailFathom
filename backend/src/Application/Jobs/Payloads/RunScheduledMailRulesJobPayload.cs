// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Jobs.Payloads;

/// <summary>Points one job at a whole account, and at nothing inside its mailbox.</summary>
/// <remarks>
/// <para>
/// The account identifier is the deployment's own configured name for a mailbox, so the document carries nothing derived
/// from a message and nothing that could become one: there is no property here for a folder, an occurrence, or a
/// subject. What the work reads about the mailbox it reads from committed local state.
/// </para>
/// <para>
/// It names the owner beside the identifier, because an identifier names one account within its owner and this document
/// is read by work that then writes rows about that account. The owner is generated and names nobody outside this
/// deployment, so carrying it discloses nothing an operator reading a queued job may not see.
/// </para>
/// <para>
/// The properties are primitives rather than the domain value objects they came from, because this record is the stored
/// document — one <c>jsonb</c> column an operator reads when they ask what a queued job is. Rebuilding the identity is
/// <see cref="ToAccountIdentity" />, which validates both halves the way the domain types do.
/// </para>
/// </remarks>
public sealed record RunScheduledMailRulesJobPayload : IJobPayload
{
    /// <summary>Gets the owner whose account the work is about.</summary>
    public required Guid OwnerId { get; init; }

    /// <summary>Gets the account the work is about, within that owner.</summary>
    public required string AccountId { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public JobType JobType => JobType.RunScheduledMailRules;

    /// <summary>Describes one account as the document a job carries.</summary>
    /// <param name="account">The account the work is about, named by its owner and its identifier together.</param>
    /// <returns>The payload naming that account.</returns>
    public static RunScheduledMailRulesJobPayload For(MailAccountIdentity account) => new()
    {
        OwnerId = account.Owner.Value,
        AccountId = account.Id.Value,
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
}
