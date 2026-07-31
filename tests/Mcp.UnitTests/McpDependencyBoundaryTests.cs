// Copyright © 2026 Krzysztof Kasprowicz

using System.Reflection;
using Xunit;

namespace MailMcp.Mcp.UnitTests;

/// <summary>Covers what the protocol boundary is allowed to depend on.</summary>
/// <remarks>
/// The read-only tool set reaches no chat model, and the way that is guaranteed is structural rather than by review: an
/// assembly cannot call what it does not reference. Asserting the reference list is therefore the whole proof, and it
/// holds for every tool this project publishes now and for every one added later.
/// </remarks>
public sealed class McpDependencyBoundaryTests
{
    /// <summary>
    /// The retrieval a search performs is lexical because nothing else is reachable from here. An embedding client, a
    /// chat client, or a retrieval pipeline would have to arrive as a project or package reference first, which is what
    /// this assertion refuses — including the case where somebody adds one to answer a question a tool description
    /// already says the tool does not answer.
    /// </summary>
    [Fact]
    public void McpAssembly_ReferencesNoAiBoundary()
    {
        // Arrange
        var mcpAssembly = Assembly.Load("MailMcp.Mcp");

        // Act
        var referencedMailMcpAssemblies = mcpAssembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .Where(name => name.StartsWith("MailMcp.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Assert
        Assert.Equal(["MailMcp.Application", "MailMcp.Domain"], referencedMailMcpAssemblies);
    }
}
