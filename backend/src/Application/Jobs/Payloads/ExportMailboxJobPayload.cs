// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Exports;

namespace MailFathom.Application.Jobs.Payloads;

/// <summary>Names the one export whose archive this work writes.</summary>
/// <remarks>
/// <para>
/// The export's identity and the account, and nothing else. What the export covers, how large it may be, and where it
/// stands are all on the export's own record, which the work reads when it starts — so a payload written by one build
/// and claimed by another carries no copy of a decision that may have moved on, and a cancellation an operator wrote
/// after the job was queued is seen by the attempt rather than argued with.
/// </para>
/// <para>
/// The identity is the whole of the idempotency key, which is what makes the work run once: an export is recorded
/// before the job is queued, so there is exactly one job per export however many times an enqueue is repeated.
/// </para>
/// </remarks>
public sealed record ExportMailboxJobPayload : IJobPayload
{
    /// <summary>Gets the account's generated identifier.</summary>
    public required string AccountId { get; init; }

    /// <summary>Gets the export whose archive is written.</summary>
    public required Guid ExportId { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public JobType JobType => JobType.ExportMailbox;

    /// <summary>Gets the account whose mailbox is carried out.</summary>
    [JsonIgnore]
    public MailAccountId Account => MailAccountId.Create(this.AccountId);

    /// <summary>Gets the export the work belongs to.</summary>
    [JsonIgnore]
    public MailboxExportId Export => MailboxExportId.Create(this.ExportId);

    /// <summary>States the work one recorded export owes.</summary>
    /// <param name="account">The account whose mailbox is exported.</param>
    /// <param name="export">The export recorded for it.</param>
    /// <returns>The payload.</returns>
    public static ExportMailboxJobPayload For(MailAccountId account, MailboxExportId export) => new()
    {
        AccountId = account.Value,
        ExportId = export.Value,
    };

    /// <summary>Composes the identity that makes one export's archive be written once.</summary>
    /// <returns>The key.</returns>
    /// <remarks>An export identity is a UUID, so the composed key is a fixed length well inside what the store accepts and needs no digest to fit.</remarks>
    public JobIdempotencyKey ToIdempotencyKey() =>
        JobIdempotencyKey.Create($"{JobType.ExportMailbox.Name}:{this.ExportId}");
}
