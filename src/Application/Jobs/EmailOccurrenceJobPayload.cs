// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Jobs;

/// <summary>Points one job at a single stored message occurrence, and at nothing inside the message.</summary>
/// <remarks>
/// <para>
/// The four components are the stable remote occurrence identity — account, folder binding, UIDVALIDITY, and UID — so
/// a handler resolves what it needs from committed local state rather than from anything the enqueuer copied. A subject,
/// an address, a body, and extracted text are all absent by construction: there is no property to put one in.
/// </para>
/// <para>
/// The folder is named by its alias and the generation that alias was bound under rather than by the local key of the
/// row, because the identity is only stable while its folder component identifies one specific remote folder, and an
/// alias can be repointed to another one.
/// </para>
/// <para>
/// The properties are primitives rather than the domain value objects they came from, because this record is the stored
/// document: it is serialized into one <c>jsonb</c> column and read by an operator looking at a queue. Rebuilding the
/// identity is <see cref="ToOccurrenceId" />, which validates every component the way the domain types do.
/// </para>
/// </remarks>
public sealed record EmailOccurrenceJobPayload : IJobPayload
{
    /// <summary>Gets the account whose mailbox the occurrence belongs to.</summary>
    public required string AccountId { get; init; }

    /// <summary>Gets the operator-facing name of the folder the occurrence was read in.</summary>
    public required string FolderAlias { get; init; }

    /// <summary>Gets which binding of that alias the occurrence belongs to.</summary>
    public required int FolderResolutionGeneration { get; init; }

    /// <summary>Gets the UIDVALIDITY the folder advertised when the occurrence was stored.</summary>
    public required uint UidValidity { get; init; }

    /// <summary>Gets the UID the occurrence carries within that UIDVALIDITY scope.</summary>
    public required uint Uid { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public JobType JobType => JobType.ClassifyEmailSpam;

    /// <summary>Describes one occurrence as the document a job carries.</summary>
    /// <param name="occurrence">The stable remote occurrence identity.</param>
    /// <returns>The payload naming that occurrence.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="occurrence" /> is <see langword="null" />.</exception>
    public static EmailOccurrenceJobPayload For(EmailOccurrenceId occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);

        return new EmailOccurrenceJobPayload
        {
            AccountId = occurrence.AccountId.Value,
            FolderAlias = occurrence.FolderResolutionId.Alias.Value,
            FolderResolutionGeneration = occurrence.FolderResolutionId.Generation.Value,
            UidValidity = occurrence.UidValidity.Value,
            Uid = occurrence.Uid.Value,
        };
    }

    /// <summary>Rebuilds the occurrence identity this payload names.</summary>
    /// <returns>The stable remote occurrence identity.</returns>
    /// <exception cref="ArgumentException">Thrown when a stored component no longer names a valid identity.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a stored numeric component is outside the range its domain type allows.</exception>
    /// <remarks>
    /// A stored document that no longer parses is refused rather than repaired. Every component was written from a
    /// validated identity, so a value that fails here describes a row nothing can act on, and reconstructing a
    /// plausible identity from it would point the work at a different message.
    /// </remarks>
    public EmailOccurrenceId ToOccurrenceId() => EmailOccurrenceId.Create(
        MailAccountId.Create(this.AccountId),
        new MailFolderResolutionId(
            MailFolderAlias.Create(this.FolderAlias),
            MailFolderResolutionGeneration.Create(this.FolderResolutionGeneration)),
        ImapUidValidity.Create(this.UidValidity),
        ImapUid.Create(this.Uid));
}
