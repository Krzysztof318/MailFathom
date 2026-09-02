// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using MailFathom.Domain.Failures;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.AI.UnitTests;

/// <summary>Covers the failure contract the AI boundary is bound by like every other assembly.</summary>
public sealed class AiFailureContractTests
{
    /// <summary>
    /// A failure outside the hierarchy carries no code a boundary can report and obeys no stated message contract.
    /// The assembly is loaded by name because it declares no type yet to anchor a <see langword="typeof" /> on, and
    /// the assertion has to exist before the first exception is written rather than after.
    /// </summary>
    [Fact]
    public void AiAssembly_EveryDeclaredException_DerivesFromMailFathomException()
    {
        // Arrange
        var aiAssembly = Assembly.Load("MailFathom.AI");

        // Act, Assert
        ExceptionHierarchyAssertion.AssertEveryDeclaredExceptionDerivesFrom(aiAssembly, typeof(MailFathomException));
    }
}
