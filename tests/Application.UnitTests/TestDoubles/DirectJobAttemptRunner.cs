// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Runs an attempt through a real executor, with none of the isolation the composition root supplies.</summary>
/// <remarks>
/// The port exists so each attempt gets a persistence scope of its own, and a scope is the one thing a unit test has
/// nothing to isolate: the store behind these tests is a substitute rather than a session. Running the real executor is
/// what keeps a pass's tests about what a pass does with the outcomes it collects.
/// </remarks>
internal sealed class DirectJobAttemptRunner(JobExecutor executor) : IJobAttemptRunner
{
    /// <inheritdoc />
    public Task<JobExecutionResult> RunAsync(LeasedJob job, CancellationToken stoppingToken) =>
        executor.ExecuteAsync(job, stoppingToken);
}
