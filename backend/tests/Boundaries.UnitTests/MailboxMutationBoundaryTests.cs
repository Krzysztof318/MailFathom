// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using MailFathom.Application.Mail.Mutations;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace MailFathom.Boundaries.UnitTests;

/// <summary>Covers which boundaries can obtain the one capability able to change a remote mailbox.</summary>
/// <remarks>
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md" />
/// draws the mutation boundary around the write session: the use case that performs a mutation obtains one, and
/// nothing else does. <c>McpDependencyBoundaryTests</c> asserts the same claim for the protocol boundary through
/// reflection, which reaches a member's signature and the fields of a compiler-generated closure, and therefore
/// reaches a session held across an <c>await</c> or captured by a lambda but not one resolved, used, and discarded
/// inside a single method. That is the gap this rule closes, and it closes it for every assembly rather than for one.
/// </remarks>
public sealed class MailboxMutationBoundaryTests
{
    [Fact]
    public void WriteSessionCapability_OutsideTheMutationBoundary_IsUnreachable()
    {
        // Arrange
        var outsideTheMutationBoundary = Types()
            .That()
            .DoNotResideInAssembly(CompiledBoundaries.Application, CompiledBoundaries.Infrastructure)
            .As("outside the mutation boundary");

        IArchRule theMutationCapabilityStaysInside = Types()
            .That()
            .Are(outsideTheMutationBoundary)
            .Should()
            .NotDependOnAny(typeof(IMailboxWriteSession), typeof(IMailboxWriteSessionFactory))
            .Because(
                "a write session is the only capability able to set a flag on the server, so an MCP read, an "
                    + "answering run, or an administrative command that could obtain one could mark mail read while "
                    + "serving what its caller asked to be a read");

        // Act & Assert
        theMutationCapabilityStaysInside.Check(CompiledBoundaries.Solution);
    }
}
