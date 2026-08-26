// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Scheduling;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Scheduling;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Holds recurring-send declarations in memory, with the identity and stopping rules the real store enforces.</summary>
/// <remarks>
/// It reproduces what the callers above depend on and nothing else: one declaration per idempotency identity, a stopped
/// declaration that keeps its row and is read by nothing that dispatches, and an occasion that never moves backwards.
/// The unique index that makes the first of those true against a real database is what the integration suite proves;
/// here the dictionary stands in for it.
/// </remarks>
/// <param name="timeProvider">Stamps what the store writes. A test that supplies none gets a clock standing still at the Unix epoch, never the machine's.</param>
internal sealed class InMemoryRecurringSendStore(TimeProvider? timeProvider = null) : IRecurringSendStore
{
    private readonly Dictionary<(string Account, OutgoingEmailOrigin Origin, string Identity), RecurringSendId>
        identities = [];

    private readonly Dictionary<RecurringSendId, RecurringSend> declarations = [];
    private readonly TimeProvider clock = timeProvider ?? new FakeTimeProvider(DateTimeOffset.UnixEpoch);

    /// <summary>Gets every declaration the store holds, stopped ones included.</summary>
    internal IReadOnlyCollection<RecurringSend> Declarations => this.declarations.Values;

    /// <summary>Writes a declaration the way a caller that has already committed left it, without going through a session.</summary>
    /// <param name="declaration">The declaration to hold.</param>
    /// <returns>The declaration now in the store.</returns>
    internal RecurringSend Publish(RecurringSend declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        this.identities[IdentityOf(declaration.AccountId.Value, declaration.Requester)] = declaration.Id;
        this.declarations[declaration.Id] = declaration;

        return declaration;
    }

    /// <summary>Reads back exactly what the store holds for one declaration.</summary>
    /// <param name="recurringSendId">The declaration to read.</param>
    /// <returns>The declaration.</returns>
    internal RecurringSend Read(RecurringSendId recurringSendId) => this.declarations[recurringSendId];

    public Task<RecurringSend> DeclareAsync(
        IPersistenceSession session,
        RecurringSendRequest request,
        long draftByteLength,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(draftByteLength);

        var identity = IdentityOf(request.Account.Id.Value, request.Requester);

        if (this.identities.TryGetValue(identity, out var existing))
        {
            return Task.FromResult(this.declarations[existing]);
        }

        var declared = new RecurringSend
        {
            Id = RecurringSendId.Create(Guid.CreateVersion7()),
            Account = request.Account,
            Requester = request.Requester,
            Recipients = request.Recipients,
            Schedule = request.Schedule,
            DraftByteLength = draftByteLength,
            DeclaredAt = this.clock.GetUtcNow(),
        };

        return Task.FromResult(this.Publish(declared));
    }

    public Task<RecurringSend?> FindAsync(RecurringSendId recurringSendId, CancellationToken cancellationToken) =>
        Task.FromResult(this.declarations.TryGetValue(recurringSendId, out var declaration) ? declaration : null);

    public Task<IReadOnlyList<RecurringSendDeclaration>> ReadActiveAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        IReadOnlyList<RecurringSendDeclaration> active =
        [
            .. this.declarations.Values
                .Where(declaration => declaration.IsActive)
                .OrderBy(declaration => declaration.DeclaredAt)
                .ThenBy(declaration => declaration.Id.Value)
                .Take(limit)
                .Select(declaration => new RecurringSendDeclaration(
                    declaration.Id,
                    MailAccountIdentity.Create(SyntheticMailOwner.Deployment, declaration.AccountId),
                    declaration.Schedule)),
        ];

        return Task.FromResult(active);
    }

    public Task<RecurringSendCancellation> CancelAsync(
        IPersistenceSession session,
        RecurringSendId recurringSendId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!this.declarations.TryGetValue(recurringSendId, out var declaration))
        {
            return Task.FromResult(RecurringSendCancellation.NotFound);
        }

        if (!declaration.IsActive)
        {
            return Task.FromResult(RecurringSendCancellation.AlreadyCancelled);
        }

        this.declarations[recurringSendId] = declaration with { CancelledAt = this.clock.GetUtcNow() };

        return Task.FromResult(RecurringSendCancellation.Cancelled);
    }

    public Task RecordOccurrenceAsync(
        IPersistenceSession session,
        RecurringSendId recurringSendId,
        DateTimeOffset occurrenceAt,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!this.declarations.TryGetValue(recurringSendId, out var declaration))
        {
            throw new InvalidOperationException($"No recurring send carries the identifier {recurringSendId}.");
        }

        // Never backwards, exactly as the real store writes it: two instances reaching one occasion write the same
        // values, and a dispatch that ran late must not take a declaration back to an occasion a later one passed.
        if (declaration.LastOccurrenceAt is { } recorded && recorded > occurrenceAt)
        {
            return Task.CompletedTask;
        }

        this.declarations[recurringSendId] = declaration with
        {
            LastOccurrenceAt = occurrenceAt,
            LastOccurrenceEmailId = outgoingEmailId,
        };

        return Task.CompletedTask;
    }

    private static (string Account, OutgoingEmailOrigin Origin, string Identity) IdentityOf(
        string accountId,
        OutgoingEmailRequester requester) =>
        (accountId, requester.Origin, requester.Identity);
}
