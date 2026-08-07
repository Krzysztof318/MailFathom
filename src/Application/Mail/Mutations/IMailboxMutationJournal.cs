// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations;

/// <summary>Carries one mutation's durable stage into and out of the session performing it.</summary>
/// <remarks>
/// <para>
/// This is what makes a non-atomic protocol sequence resumable without moving knowledge of that sequence out of the
/// adapter. Which commands a relocation is made of depends on what the connection advertises, and deciding that above
/// the port would put the protocol back into the caller; so the session announces each stage as it passes it, and reads
/// <see cref="Stage" /> to know where a resumed attempt should continue from.
/// </para>
/// <para>
/// Every method is durable before it returns. A stage that had only been remembered in memory would be exactly the
/// stage lost to the crash it exists to survive, so an announcement commits on its own rather than joining a
/// transaction the caller holds open across the mail server.
/// </para>
/// <para>
/// One journal belongs to one mutation and is used by one session at a time.
/// </para>
/// </remarks>
public interface IMailboxMutationJournal
{
    /// <summary>Gets the stage the mutation has durably reached, which a resumed attempt continues from.</summary>
    MailboxMutationStage Stage { get; }

    /// <summary>Gets where the destination folder put the email, as far as the record says.</summary>
    /// <remarks>A resumed attempt that has nothing left to do returns this rather than asking the server again for an identity it already recorded.</remarks>
    RemoteEmailPlacement Placement { get; }

    /// <summary>Records that the command placing the email in its destination folder is about to be issued.</summary>
    /// <param name="cancellationToken">Cancels the durable write.</param>
    /// <returns>A task that completes once the stage is durable.</returns>
    /// <remarks>
    /// It is announced before the command rather than after it, because the stage exists for the crash that happens
    /// while the command is in flight. A mutation found here is never issued again.
    /// </remarks>
    Task PlacementIssuedAsync(CancellationToken cancellationToken);

    /// <summary>Records that the server acknowledged the placement, and where it said the email landed.</summary>
    /// <param name="placement">What the server named, or the reported absence of a <c>COPYUID</c> response.</param>
    /// <param name="cancellationToken">Cancels the durable write.</param>
    /// <returns>A task that completes once the stage and the placement are durable.</returns>
    Task PlacementConfirmedAsync(RemoteEmailPlacement placement, CancellationToken cancellationToken);

    /// <summary>Records that the source email now carries <c>\Deleted</c> and only the expunge remains.</summary>
    /// <param name="cancellationToken">Cancels the durable write.</param>
    /// <returns>A task that completes once the stage is durable.</returns>
    Task SourceFlaggedDeletedAsync(CancellationToken cancellationToken);
}
