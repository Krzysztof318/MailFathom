// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations.Convergence;

/// <summary>States how many of one account's mutations of one kind stand in one lifecycle, and how long the oldest has.</summary>
/// <param name="Mutation">The change those records asked for.</param>
/// <param name="Lifecycle">Where in its lifecycle every record in this group stands.</param>
/// <param name="Count">How many records the group holds.</param>
/// <param name="OldestRecordedAt">When the earliest of them was written down, which is what an age is measured from.</param>
/// <remarks>
/// <para>
/// The age is carried as the instant rather than as a duration, because whoever reports it knows what time it is now
/// and this read does not. Measuring here would fix the age at whatever moment the query ran.
/// </para>
/// <para>
/// A completed mutation is not in this answer. It is counted where a completion already is — the mutation counter's
/// success outcome — and asking a growing table how many changes have ever succeeded, once per account per run, would
/// buy nothing the counter does not already say. What this exists for is the three lifecycles nothing else reports: a
/// change waiting, a change in flight, and a change that has stopped.
/// </para>
/// </remarks>
public sealed record MailboxMutationLifecycleCount(
    MailboxMutation Mutation,
    MailboxMutationLifecycle Lifecycle,
    int Count,
    DateTimeOffset OldestRecordedAt);
