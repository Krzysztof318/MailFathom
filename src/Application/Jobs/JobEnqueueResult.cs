// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs;

/// <summary>Names the job an enqueue asked for, and says whether that call is the one that created it.</summary>
/// <param name="JobId">The job carrying the requested type and key, whichever call wrote it.</param>
/// <param name="Outcome">Whether this call created the job or found one already enqueued.</param>
public sealed record JobEnqueueResult(JobId JobId, JobEnqueueOutcome Outcome);
