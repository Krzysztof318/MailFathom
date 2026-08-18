// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;

namespace MailFathom.Application.Mail.Maintenance;

/// <summary>What asking for a re-derivation did: the run the scope now has, and what the queue made of the request.</summary>
/// <param name="Run">The run the scope has outstanding, which is the one this request started or the one it found.</param>
/// <param name="Accepted">Whether this request is what started the run, rather than finding one already under way.</param>
/// <param name="QueueOutcome">What the queue did with the segment this request asked to be carried.</param>
/// <remarks>
/// The two answers are separate because they can disagree in the one case that matters. A request finding a run already
/// under way is answered with it and enqueues the segment that run is on, which the queue reports as already enqueued —
/// the ordinary case. A request that finds one whose job is no longer in the queue enqueues it again and is what puts
/// the walk back in motion, and a queue too full to accept it has taken neither decision away from the operator:
/// the run stands, and asking again is what carries it.
/// </remarks>
public sealed record StoredMailRederivationRequest(
    StoredMailRederivationRun Run,
    bool Accepted,
    JobEnqueueOutcome QueueOutcome);
