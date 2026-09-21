// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the users several suites arrange a refusal between.</summary>
/// <remarks>
/// The whole value of this helper is that they are different users and that each names somebody. Either failing would
/// turn every test asserting that one user cannot read another's mail into a test that passes without asserting
/// anything, in each of the suites that use it rather than here.
/// </remarks>
public sealed class SyntheticUserTests
{
    [Fact]
    public void EveryUser_ComparedWithTheOthers_IsADifferentPerson()
    {
        // Arrange, Act & Assert
        Assert.NotEqual(SyntheticUser.Deployment, SyntheticUser.Another);
        Assert.NotEqual(SyntheticUser.Deployment, SyntheticUser.Third);
        Assert.NotEqual(SyntheticUser.Another, SyntheticUser.Third);
    }

    [Fact]
    public void EveryUser_WhicheverOneATestArranges_NamesSomebody()
    {
        // Arrange, Act & Assert
        Assert.True(SyntheticUser.Deployment.IsSpecified);
        Assert.True(SyntheticUser.Another.IsSpecified);
        Assert.True(SyntheticUser.Third.IsSpecified);
    }

    /// <summary>Fixed rather than generated, so a failure names the same value on every run.</summary>
    [Fact]
    public void EveryUser_ReadTwice_IsTheSameValue()
    {
        // Arrange, Act & Assert
        Assert.Equal(SyntheticUser.Deployment, SyntheticUser.Deployment);
        Assert.Equal(SyntheticUser.Another, SyntheticUser.Another);
        Assert.Equal(SyntheticUser.Third, SyntheticUser.Third);
    }
}
