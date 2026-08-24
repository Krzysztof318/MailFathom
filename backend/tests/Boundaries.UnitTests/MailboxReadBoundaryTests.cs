// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Emails.Threads;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace MailFathom.Boundaries.UnitTests;

/// <summary>Covers where it is decided which mail a caller may read, and that only one place decides it.</summary>
/// <remarks>
/// <para>
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0014-single-tenant-multi-user-ownership-on-the-mail-account.md" />
/// settles ownership before a query rather than inside one, so the scope a resolver produces is the whole of what a
/// caller is entitled to and the query that reads mail has to apply all of it. A read model that narrowed the stored
/// emails itself would be a second reading of that entitlement — and one nobody would find by reading either copy,
/// because each looks complete on its own.
/// </para>
/// <para>
/// The rule reads intermediate language for the reason this project exists: the shared narrowing is a static class
/// called inside a method body, so nothing about it appears in a signature and reflection reports the reads as
/// depending on nothing. It is stated once per port rather than over a namespace so that a failure names the read that
/// went its own way, and so that a port added later is a line here rather than a silent gap.
/// </para>
/// </remarks>
public sealed class MailboxReadBoundaryTests
{
    /// <summary>The one place that decides which stored emails a scope admits.</summary>
    private const string SharedNarrowingPattern =
        @"^MailFathom\.Infrastructure\.Persistence\.Emails\.StoredEmailSelectionPredicate$";

    /// <summary>The protocol boundary, whose every tool answers one caller acting for one owner.</summary>
    private const string ProtocolBoundaryPattern = @"^MailFathom\.Mcp\.";

    [Theory]
    [InlineData(typeof(IStoredEmailTimelineReader))]
    [InlineData(typeof(IEmailSearchIndexReader))]
    [InlineData(typeof(IEmailVectorSearchIndexReader))]
    [InlineData(typeof(IEmailThreadReader))]
    public void MailboxReadModel_WhicheverPortAnswersTheRead_NarrowsThroughTheSharedPredicate(Type mailboxReadPort)
    {
        // Arrange
        Assert.Contains(
            CompiledBoundaries.Infrastructure.GetTypes(),
            candidate => !candidate.IsInterface && mailboxReadPort.IsAssignableFrom(candidate));

        IArchRule theReadNarrowsThroughTheSharedPredicate = Classes()
            .That()
            .ImplementInterface(mailboxReadPort)
            .Should()
            .DependOnAnyTypesThat()
            .HaveFullNameMatching(SharedNarrowingPattern)
            .Because(
                "which mail a caller may see is decided by the scope ownership and folder mapping settled before the "
                    + "read began, so a read model that narrowed the stored emails itself would answer that question "
                    + "a second time and could answer it differently");

        // Act & Assert
        theReadNarrowsThroughTheSharedPredicate.Check(CompiledBoundaries.Solution);
    }

    /// <summary>
    /// The two account catalogs are told apart by the members they publish rather than by a flag, and this is what
    /// makes that separation hold: a tool reaching the deployment-wide catalog would answer for every account the
    /// deployment serves whoever asked, which is precisely the read the caller-scoped catalog exists to narrow. It is
    /// stated over the whole protocol assembly rather than over the tools, because a helper the tools compose would be
    /// the same defect one indirection further out.
    /// </summary>
    [Fact]
    public void DeploymentWideAccountCatalog_AtTheProtocolBoundary_IsUnreachable()
    {
        // Arrange
        IArchRule theProtocolBoundaryReadsOnlyTheCallersOwnAccounts = Types()
            .That()
            .HaveFullNameMatching(ProtocolBoundaryPattern)
            .Should()
            .NotDependOnAny(typeof(IDeploymentMailAccountCatalog))
            .Because(
                "every MCP tool answers one caller acting for one owner, so a tool that could name the accounts the "
                    + "deployment serves would publish one person's mailbox to another's request");

        // Act & Assert
        theProtocolBoundaryReadsOnlyTheCallersOwnAccounts.Check(CompiledBoundaries.Solution);
    }
}
