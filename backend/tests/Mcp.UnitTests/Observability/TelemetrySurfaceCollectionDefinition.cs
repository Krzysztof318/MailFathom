// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Xunit;

namespace MailFathom.Mcp.UnitTests.Observability;

/// <summary>Runs the surface-wide telemetry contract on its own, because it drives every publisher in the assembly.</summary>
/// <remarks>
/// <para>
/// Every other class here drives the one publisher it is about, which is what makes a listener over the process-wide
/// meter usable at all: an assertion that a counter recorded one call, or none, holds only while nothing else is
/// recording on it. A contract over the whole surface breaks that by construction, since driving everything is the
/// point of it.
/// </para>
/// <para>
/// Turning parallelization off for this collection restores it: xUnit runs a non-parallelizable collection by itself,
/// after the ones that run together, so nothing else is publishing while this drives. The definition is declared per
/// assembly because a collection is an assembly-level thing in xUnit, not because the reasoning differs from the one
/// the Infrastructure suite states.
/// </para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TelemetrySurfaceCollectionDefinition
{
    /// <summary>The name a test class joins this collection by.</summary>
    public const string Name = "Telemetry surface";
}
