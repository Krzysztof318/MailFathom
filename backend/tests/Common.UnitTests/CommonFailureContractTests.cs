// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Common.MailboxOAuth;
using MailFathom.Domain.Failures;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Common.UnitTests;

/// <summary>Covers the failure contract the shared boundary is bound by like every other assembly.</summary>
public sealed class CommonFailureContractTests
{
    /// <summary>A failure outside the hierarchy carries no code a boundary can report and obeys no stated message contract.</summary>
    [Fact]
    public void CommonAssembly_EveryDeclaredException_DerivesFromMailFathomException()
    {
        // Arrange
        var commonAssembly = typeof(MailboxAuthorizationFailedException).Assembly;

        // Act, Assert
        ExceptionHierarchyAssertion.AssertEveryDeclaredExceptionDerivesFrom(commonAssembly, typeof(MailFathomException));
    }
}
