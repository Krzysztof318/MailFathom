// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Jobs.Payloads;

/// <summary>Points one job at a whole account, and at nothing inside its mailbox.</summary>
/// <remarks>
/// <para>
/// The account identifier is the one this deployment generated for a mailbox, so the document carries nothing derived
/// from a message and nothing that could become one: there is no property here for a folder, an occurrence, or a
/// subject. What the work reads about the mailbox it reads from committed local state.
/// </para>
/// <para>
/// It names the account by its generated identifier, which names one mailbox across the deployment, and this document
/// is read by work that then writes rows about that account. The identifier names nothing outside this
/// deployment, so carrying it discloses nothing an operator reading a queued job may not see.
/// </para>
/// <para>
/// The properties are primitives rather than the domain value objects they came from, because this record is the stored
/// document — one <c>jsonb</c> column an operator reads when they ask what a queued job is. Rebuilding the identity is
/// <see cref="ToAccountId" />, which validates it the way the domain type does.
/// </para>
/// </remarks>
public sealed record RunScheduledMailRulesJobPayload : IJobPayload
{
    /// <summary>Gets the account the work is about.</summary>
    public required string AccountId { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public JobType JobType => JobType.RunScheduledMailRules;

    /// <summary>Describes one account as the document a job carries.</summary>
    /// <param name="account">The account the work is about, named by its generated identifier.</param>
    /// <returns>The payload naming that account.</returns>
    public static RunScheduledMailRulesJobPayload For(MailAccountId account) => new()
    {
        AccountId = account.Value,
    };

    /// <summary>Rebuilds the generated account identifier this payload names.</summary>
    /// <returns>The account's generated identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when the stored value no longer names a valid account.</exception>
    /// <remarks>
    /// The identifier is a required property, so a document that carries none is refused by the deserializer before
    /// this is reached. What remains here is a value that is present and does not name an account, which this refuses
    /// for the reason every payload record refuses a component that no longer validates.
    /// </remarks>
    public MailAccountId ToAccountId() =>
        MailAccountId.Create(this.AccountId);
}
