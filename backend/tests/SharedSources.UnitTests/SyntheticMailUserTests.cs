// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the two users several suites arrange a refusal between.</summary>
/// <remarks>
/// The whole value of this helper is that the two are different users and that both name somebody. Either failing
/// would turn every test asserting that one user cannot read another's mail into a test that passes without asserting
/// anything, in each of the suites that use it rather than here.
/// </remarks>
public sealed class SyntheticMailUserTests
{
    [Fact]
    public void Deployment_AndAnother_AreDifferentUsers()
    {
        // Arrange, Act & Assert
        Assert.NotEqual(SyntheticMailUser.Deployment, SyntheticMailUser.Another);
    }

    [Fact]
    public void EveryUser_WhicheverOneATestArranges_NamesSomebody()
    {
        // Arrange, Act & Assert
        Assert.True(SyntheticMailUser.Deployment.IsSpecified);
        Assert.True(SyntheticMailUser.Another.IsSpecified);
    }

    /// <summary>Fixed rather than generated, so a failure names the same value on every run.</summary>
    [Fact]
    public void EveryUser_ReadTwice_IsTheSameValue()
    {
        // Arrange, Act & Assert
        Assert.Equal(SyntheticMailUser.Deployment, SyntheticMailUser.Deployment);
        Assert.Equal(SyntheticMailUser.Another, SyntheticMailUser.Another);
    }
}
