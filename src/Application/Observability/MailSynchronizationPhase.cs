// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Observability;

/// <summary>The stages one folder's turn through a synchronization cycle passes through, in the order it reaches them.</summary>
/// <remarks>
/// <para>
/// A folder run reports one duration for work whose parts fail and slow down for entirely different reasons: a mail
/// server that stopped answering, a local scan that got slower, a database under contention. These are the boundaries
/// that separate them, and they are the run's own stages rather than a division of its code — a run that ends early
/// reports the stages it reached and no others.
/// </para>
/// <para>
/// What is deliberately not here is a stage per message. A folder run stores as many emails as its batch bounds allow,
/// so a stage apiece would put one span per synchronized message into a trace store to say what the counters beside it
/// already say better.
/// </para>
/// </remarks>
public enum MailSynchronizationPhase
{
    /// <summary>Turning the configured alias into the folder the mail server advertises for it.</summary>
    ResolveFolder = 0,

    /// <summary>Opening the read-only mail session the rest of the run works over, and reading what it is bound to.</summary>
    OpenSession = 1,

    /// <summary>Walking the folder forward: discovering mail, retrieving what it stores, and committing the checkpoint.</summary>
    DiscoverEmails = 2,

    /// <summary>Asking the mail server for one bounded batch of the mail that follows the checkpoint.</summary>
    /// <remarks>
    /// Reached once per batch rather than once per run, which is what separates a mail server slow to list a folder from
    /// local work slow to derive from it. It is bounded by the run's batch limit, so a run publishes at most that many.
    /// </remarks>
    FetchEmailBatch = 3,

    /// <summary>Walking the window backwards for the changes a forward pass cannot see, such as mail that was removed.</summary>
    ReconcileFolder = 4,

    /// <summary>Retrieving the content of mail an earlier run recorded without it.</summary>
    RefillDeferredContent = 5,
}
