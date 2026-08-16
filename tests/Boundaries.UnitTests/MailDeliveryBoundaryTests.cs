// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using MailFathom.Application.Mail.Delivery;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace MailFathom.Boundaries.UnitTests;

/// <summary>Covers which boundaries can obtain the one capability able to send mail out of this deployment.</summary>
/// <remarks>
/// Sending is the first thing MailFathom does that leaves the deployment and cannot be undone, and the guarantee that a
/// read path cannot do it rests on the delivery session being a type a read path never holds. Reflection reaches a
/// member's signature and the fields of a compiler-generated closure, so it would miss a session obtained, used, and
/// discarded inside one method body; that is the gap this rule closes, and it closes it for every assembly.
/// </remarks>
public sealed class MailDeliveryBoundaryTests
{
    [Fact]
    public void DeliverySessionCapability_OutsideTheDeliveryBoundary_IsUnreachable()
    {
        // Arrange
        var outsideTheDeliveryBoundary = Types()
            .That()
            .DoNotResideInAssembly(CompiledBoundaries.Application, CompiledBoundaries.Infrastructure)
            .As("outside the delivery boundary");

        IArchRule theDeliveryCapabilityStaysInside = Types()
            .That()
            .Are(outsideTheDeliveryBoundary)
            .Should()
            .NotDependOnAny(typeof(IMailDeliverySession), typeof(IMailDeliverySessionFactory))
            .Because(
                "a delivery session is the only capability able to hand a message to a submission server, so an MCP "
                    + "read, an answering run, or an administrative command that could obtain one could send mail "
                    + "while serving what its caller asked to be a read");

        // Act & Assert
        theDeliveryCapabilityStaysInside.Check(CompiledBoundaries.Solution);
    }
}
