// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the two owners several suites arrange a refusal between.</summary>
/// <remarks>
/// The whole value of this helper is that the two are different owners and that both name somebody. Either failing
/// would turn every test asserting that one owner cannot read another's mail into a test that passes without asserting
/// anything, in each of the suites that use it rather than here.
/// </remarks>
public sealed class SyntheticMailOwnerTests
{
    [Fact]
    public void Deployment_AndAnother_AreDifferentOwners()
    {
        // Arrange, Act & Assert
        Assert.NotEqual(SyntheticMailOwner.Deployment, SyntheticMailOwner.Another);
    }

    [Fact]
    public void EveryOwner_WhicheverOneATestArranges_NamesSomebody()
    {
        // Arrange, Act & Assert
        Assert.True(SyntheticMailOwner.Deployment.IsSpecified);
        Assert.True(SyntheticMailOwner.Another.IsSpecified);
    }

    /// <summary>Fixed rather than generated, so a failure names the same value on every run.</summary>
    [Fact]
    public void EveryOwner_ReadTwice_IsTheSameValue()
    {
        // Arrange, Act & Assert
        Assert.Equal(SyntheticMailOwner.Deployment, SyntheticMailOwner.Deployment);
        Assert.Equal(SyntheticMailOwner.Another, SyntheticMailOwner.Another);
    }
}
