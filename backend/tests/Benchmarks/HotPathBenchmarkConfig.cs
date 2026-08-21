// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace MailFathom.Benchmarks;

/// <summary>How every benchmark in this project is run, and why it is run that way.</summary>
/// <remarks>
/// <para>
/// The run is in process. BenchmarkDotNet's default toolchain writes a project of its own beside the output and builds
/// it, and a generated project inside this repository inherits <c>backend/Directory.Build.props</c> — every analyzer, warnings
/// as errors, and a documentation file it has no comments for — so the build of the harness fails before a single
/// measurement is taken. Running in process removes the generated project rather than exempting it, at the cost the
/// library warns about: the measurement shares a process with the harness, so it is less isolated than a separate one.
/// That cost is affordable precisely because nothing gates on these numbers.
/// </para>
/// <para>
/// The run is short. Three warm-up and three measured iterations are what a trend across releases needs, and a longer
/// run would buy precision that a shared, virtualized runner cannot deliver anyway. What this report is for is a change
/// of shape over months, never a comparison between two runs an hour apart.
/// </para>
/// <para>
/// Allocation is reported beside time because the two answer different questions and only one of them is trustworthy
/// here. Allocated bytes are deterministic and are what the unit suite's budgets gate on; the timings beside them are
/// the part that belongs in a report and nowhere else.
/// </para>
/// </remarks>
internal sealed class HotPathBenchmarkConfig : ManualConfig
{
    /// <summary>Initializes the configuration every benchmark in this assembly runs under.</summary>
    public HotPathBenchmarkConfig()
    {
        this.Add(DefaultConfig.Instance);
        this.AddJob(Job.ShortRun.WithToolchain(InProcessEmitToolchain.Instance));
        this.AddDiagnoser(MemoryDiagnoser.Default);
    }
}
