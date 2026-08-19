// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Scheduling;

namespace MailFathom.Application.Mail.Delivery.Scheduling;

/// <summary>Declares one recurring dispatch for every message an owner asked to have sent again.</summary>
/// <remarks>
/// <para>
/// The declarations are read from the database rather than from configuration, because that is where they are made: an
/// owner writes a message and names a repetition, and neither is something an operator's file could hold. What the
/// dispatch mechanism gets is the same shape a configured schedule gives it, so a message that repeats reaches the same
/// worker, the same occasion arithmetic, the same one-run-at-a-time answer, and the same capacity bounds as a rule that
/// does.
/// </para>
/// <para>
/// A declaration whose schedule no longer parses is left out rather than raised over. The syntax was read where the
/// declaration was made, so a stored schedule that no longer parses is either a payload that was damaged or a build
/// whose syntax has moved, and dispatching an occasion nobody can resolve is worse than dispatching none: what a
/// deployment sees is a repetition that stopped, which is what the record itself says.
/// </para>
/// <para>
/// A cancelled declaration is not read at all, so it declares nothing from the moment it is stopped — including the
/// occasion it would otherwise have produced next. The row it leaves keeps what it last did, which is what makes the
/// stopping readable afterwards.
/// </para>
/// </remarks>
public sealed class RecurringSendScheduleSource : IScheduledJobSource
{
    /// <summary>The word every schedule this source declares is prefixed with, so a key says what declared it.</summary>
    private const string IdentityPrefix = "recurring-send";

    private readonly IRecurringSendStore recurringSends;

    /// <summary>Initializes the source over the declarations this deployment holds.</summary>
    /// <param name="recurringSends">Reads the declarations that still produce occurrences.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="recurringSends" /> is <see langword="null" />.</exception>
    public RecurringSendScheduleSource(IRecurringSendStore recurringSends)
    {
        ArgumentNullException.ThrowIfNull(recurringSends);

        this.recurringSends = recurringSends;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScheduledJob>> ReadSchedulesAsync(CancellationToken cancellationToken)
    {
        var declarations = await this.recurringSends.ReadActiveAsync(
            RecurringSendBounds.MaximumActiveDeclarations,
            cancellationToken);

        return [.. declarations.Select(Declare).OfType<ScheduledJob>()];
    }

    /// <summary>Turns one declaration into the recurring dispatch it asks for, or into nothing when its schedule is unreadable.</summary>
    private static ScheduledJob? Declare(RecurringSendDeclaration declaration) =>
        JobRecurrence.TryParse(declaration.Schedule, out var recurrence, out _)
            ? new ScheduledJob(
                JobScheduleId.Create(
                    string.Create(CultureInfo.InvariantCulture, $"{IdentityPrefix}:{declaration.Id}")),
                RecurringSendJobPayload.For(declaration.AccountId, declaration.Id),
                recurrence!,
                declaration.AccountId)
            : null;
}
