// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Configuration;
using MailFathom.Domain.Failures;
using Xunit;

namespace MailFathom.Application.UnitTests.Configuration;

/// <summary>
/// Covers what a write answers with. The version is meaningful either way and that is the contract worth asserting:
/// a refused caller reads the version now in force out of the same property a committed one reads the new version
/// from, so nothing has to know which shape it is holding to know what the deployment is running on.
/// </summary>
public sealed class ConfigurationWriteResultTests
{
    /// <summary>A committed write reports the version it produced and names no failure.</summary>
    [Fact]
    public void Committed_AVersion_ReportsItAndNoRefusal()
    {
        // Act
        var result = ConfigurationWriteResult.Committed(version: 8);

        // Assert
        Assert.True(result.IsCommitted);
        Assert.Equal(8, result.Version);
        Assert.False(result.Refusal.IsSpecified);
        Assert.Empty(result.RefusalMessages);
    }

    /// <summary>A refused write reports the version still serving, so the caller composes its next attempt over it.</summary>
    [Fact]
    public void Refused_AFailure_ReportsTheVersionStillInForce()
    {
        // Act
        var result = ConfigurationWriteResult.Refused(
            MailFathomErrorCode.ConfigurationVersionSuperseded,
            versionInForce: 4,
            ["another writer committed first"]);

        // Assert
        Assert.False(result.IsCommitted);
        Assert.Equal(4, result.Version);
        Assert.Equal(MailFathomErrorCode.ConfigurationVersionSuperseded, result.Refusal);
        Assert.Equal("another writer committed first", Assert.Single(result.RefusalMessages));
    }

    /// <summary>Every refused setting travels, because an operator correcting one at a time learns the next only by writing again.</summary>
    [Fact]
    public void Refused_SeveralSettings_CarriesEveryMessage()
    {
        // Act
        var result = ConfigurationWriteResult.Refused(
            MailFathomErrorCode.ConfigurationCandidateInvalid,
            versionInForce: 2,
            ["first", "second"]);

        // Assert
        Assert.Equal(["first", "second"], result.RefusalMessages);
    }

    /// <summary>A refusal that names no failure carries no identity a surface could report, so it is refused itself.</summary>
    [Fact]
    public void Refused_NoFailure_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            ConfigurationWriteResult.Refused(refusal: default, versionInForce: 1, ["something"]));
    }

    /// <summary>A refusal that says nothing to correct leaves the operator with no next step, so it is refused too.</summary>
    [Fact]
    public void Refused_NoMessages_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => ConfigurationWriteResult.Refused(
            MailFathomErrorCode.ConfigurationCandidateInvalid,
            versionInForce: 1,
            []));
    }

    /// <summary>A commit produces a version, so a version no commit could have produced is refused.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Committed_AVersionNoCommitProduces_IsRefused(long version)
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => ConfigurationWriteResult.Committed(version));
    }
}
