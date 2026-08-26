// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Jobs.Scheduling;

/// <summary>One execution a deployment wants repeated, and the occasions it wants it on.</summary>
/// <remarks>
/// <para>
/// The payload is fixed for the life of the declaration rather than composed per occasion, because what a recurring
/// dispatch repeats is one piece of work: the occasion decides <em>when</em>, and the identity of the execution is the
/// schedule plus that instant. A payload that varied per occasion would be a different job type asking for a scheduler
/// rather than a schedule.
/// </para>
/// <para>
/// Declared rather than stored, whichever source declared it. A deployment's configuration is one source of schedules
/// and the declarations an owner made are another, so what exists is whatever the sources answer with on the pass that
/// asks. The only durable state a schedule itself has is the occasion it last dispatched — which is state rather than
/// settings, and lives where every other piece of state does.
/// </para>
/// </remarks>
/// <param name="Id">The identity the schedule's durable state is keyed by.</param>
/// <param name="Payload">The references the repeated work is described by, which also names its job type.</param>
/// <param name="Recurrence">The occasions the work is dispatched on.</param>
/// <param name="Account">The account the work belongs to, named by its owner and its identifier, or <see langword="null" /> when it belongs to none.</param>
public sealed record ScheduledJob(
    JobScheduleId Id,
    IJobPayload Payload,
    JobRecurrence Recurrence,
    MailAccountIdentity? Account = null);
