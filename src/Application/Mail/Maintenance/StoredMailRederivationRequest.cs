// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Maintenance;

/// <summary>What asking for a re-derivation did: the run the scope now has, and what is carrying it.</summary>
/// <param name="Run">The run the scope has outstanding, which is the one this request started or the one it found.</param>
/// <param name="Accepted">Whether this request is what started the run, rather than finding one already under way.</param>
/// <param name="Carriage">What is carrying the segment the run is on, which is what the operator acts on.</param>
/// <remarks>
/// The two answers are separate because they can disagree in the cases that matter. A request finding a run already
/// under way is answered with it and enqueues the segment that run is on, which is the ordinary case and is carried. A
/// request that finds one whose job left the queue enqueues it again and is what puts the walk back in motion, while a
/// queue too full to accept it has taken nothing away from the operator: the run stands, and asking again carries it.
/// The one that needs a different act is a run whose segment is there and will never be attempted again, which
/// <see cref="StoredMailRederivationCarriage.Stopped" /> is what names.
/// </remarks>
public sealed record StoredMailRederivationRequest(
    StoredMailRederivationRun Run,
    bool Accepted,
    StoredMailRederivationCarriage Carriage);
