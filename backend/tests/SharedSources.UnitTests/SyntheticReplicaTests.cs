// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the replica several suites arrange when which replica answered is not what the test is about.</summary>
/// <remarks>
/// A status answer names the replica that composed it, so a suite comparing that name against this helper is asserting
/// nothing unless the helper is a stable value that names a process. Both claims fail here rather than in each of the
/// suites that use it.
/// </remarks>
public sealed class SyntheticReplicaTests
{
    [Fact]
    public void Answering_TheReplicaATestArranges_NamesAProcess()
    {
        // Arrange, Act & Assert
        Assert.False(string.IsNullOrWhiteSpace(SyntheticReplica.Answering.Value));
    }

    /// <summary>Fixed rather than generated, so a failure names the same value on every run.</summary>
    [Fact]
    public void Answering_ReadTwice_IsTheSameValue()
    {
        // Arrange, Act & Assert
        Assert.Equal(SyntheticReplica.Answering, SyntheticReplica.Answering);
    }
}
