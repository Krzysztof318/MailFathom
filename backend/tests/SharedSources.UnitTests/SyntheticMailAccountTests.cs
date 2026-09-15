// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the two mailboxes every suite bounding one against another arranges with.</summary>
/// <remarks>
/// The whole contract is that they are two, and fixed. A helper answering one identifier for both would make every
/// test that proves a mailbox is not reachable from another pass while nothing was ever separated, and one generating
/// them per call would report a different value on every failure.
/// </remarks>
public sealed class SyntheticMailAccountTests
{
    [Fact]
    public void Deployment_AndAnother_AreTwoDifferentMailboxes()
    {
        // Act, Assert
        Assert.NotEqual(SyntheticMailAccount.Deployment, SyntheticMailAccount.Another);
    }

    [Fact]
    public void EveryMailbox_ReadTwice_IsTheSameIdentifier()
    {
        // Act, Assert
        Assert.Equal(SyntheticMailAccount.Deployment, SyntheticMailAccount.Deployment);
        Assert.Equal(SyntheticMailAccount.Another, SyntheticMailAccount.Another);
    }

    /// <summary>A mailbox naming nothing is what several ports refuse, so neither of these may read as one.</summary>
    [Fact]
    public void EveryMailbox_IsNamed()
    {
        // Act, Assert
        Assert.NotEmpty(SyntheticMailAccount.Deployment.Value);
        Assert.NotEmpty(SyntheticMailAccount.Another.Value);
    }
}
