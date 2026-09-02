// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Runs the surface-wide telemetry contract on its own, because it drives every publisher in the assembly.</summary>
/// <remarks>
/// <para>
/// Every other class here drives the one publisher it is about, which is what makes a listener over the process-wide
/// meter usable at all: two classes never publish to the same instrument at the same moment, so a read filtered by
/// instrument and by account sees only its own. A contract over the whole surface breaks that arrangement by
/// construction — it drives all of them — and an assertion elsewhere that a counter recorded nothing would then fail
/// on this suite's traffic rather than on a defect.
/// </para>
/// <para>
/// Turning parallelization off for this collection is what restores it: xUnit runs a non-parallelizable collection by
/// itself, after the ones that run together, so nothing else is publishing while this drives. It also settles what the
/// drive leaves behind — an observable gauge stays registered on the process-wide meter for the life of the run, and
/// running last is what means nobody observes one of this suite's afterwards.
/// </para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TelemetrySurfaceCollectionDefinition
{
    /// <summary>The name a test class joins this collection by.</summary>
    public const string Name = "Telemetry surface";
}
