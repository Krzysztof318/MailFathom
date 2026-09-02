// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Emails.Threads;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Application.Mail.Delivery.Submission;
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
    private const string ProtocolBoundaryNamespace = "MailFathom.Mcp.";

    /// <summary>What the traversal below follows, which is this solution's own code and nothing it is built on.</summary>
    private const string SolutionNamespace = "MailFathom.";

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
    /// The two account catalogs are told apart by the members they publish rather than by a flag, and this holds one
    /// half of that separation: nothing an MCP call reaches names the deployment-wide catalog, so no tool can answer
    /// one caller out of every account the deployment serves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is stated over everything a call reaches rather than over the protocol assembly's own dependencies, because
    /// a tool composes a use case and that use case composes others. <c>send_email</c> resolves its account one hop
    /// away in <c>AuthoredMailSubmission</c>, and vouches for its recipients two hops further down in
    /// <c>RecipientVouching</c>, so a rule reading the assembly's own dependencies alone would be green while a use
    /// case behind a tool read the deployment's whole account set.
    /// </para>
    /// <para>
    /// The traversal follows this solution's own types and stops at everything it is built on, which is what keeps the
    /// closure the composition rather than the framework. It admits the caller-scoped adapter by never reaching it: the
    /// implementation that holds both catalogs is named by the container rather than by any use case, so a tool reaches
    /// the port and never the type behind it.
    /// </para>
    /// </remarks>
    [Fact]
    public void DeploymentWideAccountCatalog_InEverythingAnMcpCallReaches_IsAbsent()
    {
        // Arrange
        var reachedFromTheProtocolBoundary = ReachedFromTheProtocolBoundary();

        // The guard: a closure that stopped at the assembly's own types would satisfy the rule while proving nothing,
        // so the two the ADR names — the send one hop out and the vouching two further — are asserted to be in it.
        Assert.Contains(
            reachedFromTheProtocolBoundary,
            type => type.FullName == typeof(AuthoredMailSubmission).FullName);
        Assert.Contains(
            reachedFromTheProtocolBoundary,
            type => type.FullName == typeof(RecipientVouching).FullName);

        IArchRule nothingAnMcpCallReachesNamesTheDeploymentCatalog = Types()
            .That()
            .Are(reachedFromTheProtocolBoundary)
            .Should()
            .NotDependOnAny(typeof(IDeploymentMailAccountCatalog))
            .Because(
                "every MCP tool answers one caller acting for one owner, so a use case behind one that named the "
                    + "accounts the deployment serves would resolve one person's mailbox for another's request; the "
                    + "caller-scoped catalog is what a caller-facing resolution reads");

        // Act & Assert
        nothingAnMcpCallReachesNamesTheDeploymentCatalog.Check(CompiledBoundaries.Solution);
    }

    /// <summary>Walks out from the protocol assembly to every type of this solution an MCP call can reach.</summary>
    /// <remarks>
    /// Breadth rather than depth, because what matters is membership rather than the path that found it, and a
    /// composition graph with a cycle in it would not terminate without the set the walk already visited.
    /// </remarks>
    private static IReadOnlyList<IType> ReachedFromTheProtocolBoundary()
    {
        var reached = new HashSet<IType>();
        var pending = new Queue<IType>(
            CompiledBoundaries.Solution.Types.Where(type =>
                type.FullName.StartsWith(ProtocolBoundaryNamespace, StringComparison.Ordinal)));

        while (pending.Count > 0)
        {
            var type = pending.Dequeue();

            if (!reached.Add(type))
            {
                continue;
            }

            foreach (var target in type.Dependencies
                .Select(dependency => dependency.Target)
                .Where(target => target.FullName.StartsWith(SolutionNamespace, StringComparison.Ordinal)))
            {
                pending.Enqueue(target);
            }
        }

        return [.. reached];
    }
}
