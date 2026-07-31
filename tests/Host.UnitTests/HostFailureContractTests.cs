// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Domain.Failures;
using MailFathom.Host.Configuration;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>Covers the failure contract the composition root is bound by like every other assembly.</summary>
public sealed class HostFailureContractTests
{
    /// <summary>A failure outside the hierarchy carries no code a boundary can report and obeys no stated message contract.</summary>
    [Fact]
    public void HostAssembly_EveryDeclaredException_DerivesFromMailFathomException()
    {
        // Arrange
        var hostAssembly = typeof(PersistenceOptions).Assembly;

        // Act, Assert
        ExceptionHierarchyAssertion.AssertEveryDeclaredExceptionDerivesFrom(hostAssembly, typeof(MailFathomException));
    }
}
