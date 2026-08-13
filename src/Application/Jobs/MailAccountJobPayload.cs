// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Jobs;

/// <summary>Points one job at a whole account, and at nothing inside its mailbox.</summary>
/// <remarks>
/// <para>
/// The account identifier is the deployment's own configured name for a mailbox, so the document carries nothing derived
/// from a message and nothing that could become one: there is no property here for a folder, an occurrence, or a
/// subject. What the work reads about the mailbox it reads from committed local state.
/// </para>
/// <para>
/// The property is a primitive rather than the domain value object it came from, because this record is the stored
/// document — one <c>jsonb</c> column an operator reads when they ask what a queued job is. Rebuilding the identity is
/// <see cref="ToAccountId" />, which validates it the way the domain type does.
/// </para>
/// </remarks>
public sealed record MailAccountJobPayload : IJobPayload
{
    /// <summary>Gets the account the work is about.</summary>
    public required string AccountId { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public JobType JobType => JobType.RunScheduledMailRules;

    /// <summary>Describes one account as the document a job carries.</summary>
    /// <param name="accountId">The account the work is about.</param>
    /// <returns>The payload naming that account.</returns>
    public static MailAccountJobPayload For(MailAccountId accountId) => new() { AccountId = accountId.Value };

    /// <summary>Rebuilds the account identity this payload names.</summary>
    /// <returns>The account identity.</returns>
    /// <exception cref="ArgumentException">Thrown when the stored value no longer names a valid account identity.</exception>
    public MailAccountId ToAccountId() => MailAccountId.Create(this.AccountId);
}
