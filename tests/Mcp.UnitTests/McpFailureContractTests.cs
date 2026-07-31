// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Reflection;
using MailFathom.Domain.Failures;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Mcp.UnitTests;

/// <summary>Covers the failure contract the MCP boundary is bound by like every other assembly.</summary>
public sealed class McpFailureContractTests
{
    /// <summary>
    /// A failure outside the hierarchy carries no code a boundary can report and obeys no stated message contract.
    /// The assembly is loaded by name because it declares no type yet to anchor a <see langword="typeof" /> on, and
    /// the assertion has to exist before the first exception is written rather than after.
    /// </summary>
    [Fact]
    public void McpAssembly_EveryDeclaredException_DerivesFromMailFathomException()
    {
        // Arrange
        var mcpAssembly = Assembly.Load("MailFathom.Mcp");

        // Act, Assert
        ExceptionHierarchyAssertion.AssertEveryDeclaredExceptionDerivesFrom(mcpAssembly, typeof(MailFathomException));
    }
}
