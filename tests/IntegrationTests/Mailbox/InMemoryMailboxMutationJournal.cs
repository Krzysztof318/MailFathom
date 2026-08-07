// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Domain.Mutations;

namespace MailFathom.IntegrationTests.Mailbox;

/// <summary>Carries a mutation's stage for a test that is about the mail server rather than about the record.</summary>
/// <remarks>
/// The tests that are about the record itself go through the real performer and the real store against real PostgreSQL,
/// which is the only place the durability claim can be proven. This exists for the protocol tests beside them: they open
/// a write session directly to control what the server advertises, and a session needs somewhere to announce its stages
/// to. Starting one at a stage is also how those tests express a process that stopped mid-sequence.
/// </remarks>
internal sealed class InMemoryMailboxMutationJournal(
    MailboxMutationStage resumedFrom = MailboxMutationStage.Recorded,
    RemoteEmailPlacement? recordedPlacement = null,
    bool requiresSourceRemoval = false) : IMailboxMutationJournal
{
    /// <inheritdoc />
    public MailboxMutationStage Stage { get; private set; } = resumedFrom;

    /// <inheritdoc />
    public RemoteEmailPlacement Placement { get; private set; } =
        recordedPlacement ?? RemoteEmailPlacement.NotReported();

    /// <inheritdoc />
    public bool RequiresSourceRemoval { get; private set; } = requiresSourceRemoval;

    /// <inheritdoc />
    public Task PlacementIssuedAsync(bool requiresSourceRemoval, CancellationToken cancellationToken)
    {
        this.Stage = MailboxMutationStage.PlacementIssued;
        this.RequiresSourceRemoval = requiresSourceRemoval;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PlacementConfirmedAsync(RemoteEmailPlacement placement, CancellationToken cancellationToken)
    {
        this.Stage = MailboxMutationStage.PlacementConfirmed;
        this.Placement = placement;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SourceFlaggedDeletedAsync(CancellationToken cancellationToken)
    {
        this.Stage = MailboxMutationStage.SourceFlaggedDeleted;

        return Task.CompletedTask;
    }
}
