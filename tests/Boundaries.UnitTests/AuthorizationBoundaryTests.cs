// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace MailFathom.Boundaries.UnitTests;

/// <summary>Covers what the layers below the transport are allowed to know about who is calling.</summary>
/// <remarks>
/// <para>
/// ADR 0012 settles that the application layer receives an application-owned description of the principal and nothing
/// from the frameworks that produced it. Two of the three types that could carry one are already unreachable from
/// <c>Application</c> and <c>Domain</c> by project reference: ASP.NET Core and the MCP SDK are not referenced there at
/// all, and <c>ApplicationDependencyBoundaryTests</c> asserts that exact reference set.
/// </para>
/// <para>
/// <c>System.Security.Claims</c> is the one that no reference set can refuse, because it ships in the shared framework
/// and is therefore compilable from every project in the solution. A rule is the only thing that keeps it out, and it
/// belongs here rather than beside the reference assertions because a claims type obtained inside a method body and
/// never stored is invisible to reflection over signatures.
/// </para>
/// </remarks>
public sealed class AuthorizationBoundaryTests
{
    private const string ApplicationAndDomainPattern = @"^MailFathom\.(Application|Domain)\.";

    private const string ClaimsPattern = @"^System\.Security\.Claims\.";

    [Fact]
    public void ClaimsTypes_InApplicationAndDomain_AreUnreachable()
    {
        // Arrange
        IArchRule claimsStayAboveTheApplicationLayer = Types()
            .That()
            .HaveFullNameMatching(ApplicationAndDomainPattern)
            .Should()
            .NotDependOnAnyTypesThat()
            .HaveFullNameMatching(ClaimsPattern)
            .Because(
                "a use case is told who it is running for through MailFathom's own AuthorizedPrincipal, so a "
                    + "ClaimsPrincipal, a ClaimsIdentity, or a Claim appearing below the transport would put an "
                    + "authentication framework's vocabulary into a layer that has to outlive it");

        // Act & Assert
        claimsStayAboveTheApplicationLayer.Check(CompiledBoundaries.Solution);
    }
}
