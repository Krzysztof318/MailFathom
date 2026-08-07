// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Domain.Mutations;

namespace MailFathom.Infrastructure.UnitTests.TestDoubles;

/// <summary>Stands in for the durable record, keeping the stages a write session announced in the order it announced them.</summary>
/// <remarks>
/// A resumed attempt is arranged by starting one of these at the stage a previous attempt left behind, which is what
/// lets a crash between two IMAP commands be expressed without a database: the session reads the same stage it would
/// have read from a real record and continues from it.
/// </remarks>
internal sealed class RecordingMailboxMutationJournal : IMailboxMutationJournal
{
    private readonly List<MailboxMutationStage> announcedStages = [];

    internal RecordingMailboxMutationJournal(
        MailboxMutationStage resumedFrom = MailboxMutationStage.Recorded,
        RemoteEmailPlacement? recordedPlacement = null)
    {
        this.Stage = resumedFrom;
        this.Placement = recordedPlacement ?? RemoteEmailPlacement.NotReported();
    }

    /// <inheritdoc />
    public MailboxMutationStage Stage { get; private set; }

    /// <inheritdoc />
    public RemoteEmailPlacement Placement { get; private set; }

    /// <summary>Gets the stages this attempt announced, in order.</summary>
    internal IReadOnlyList<MailboxMutationStage> AnnouncedStages => this.announcedStages;

    /// <inheritdoc />
    public Task PlacementIssuedAsync(CancellationToken cancellationToken) =>
        this.AdvanceAsync(MailboxMutationStage.PlacementIssued, placement: null);

    /// <inheritdoc />
    public Task PlacementConfirmedAsync(RemoteEmailPlacement placement, CancellationToken cancellationToken) =>
        this.AdvanceAsync(MailboxMutationStage.PlacementConfirmed, placement);

    /// <inheritdoc />
    public Task SourceFlaggedDeletedAsync(CancellationToken cancellationToken) =>
        this.AdvanceAsync(MailboxMutationStage.SourceFlaggedDeleted, placement: null);

    private Task AdvanceAsync(MailboxMutationStage stage, RemoteEmailPlacement? placement)
    {
        this.announcedStages.Add(stage);
        this.Stage = stage;

        if (placement is not null)
        {
            this.Placement = placement;
        }

        return Task.CompletedTask;
    }
}
