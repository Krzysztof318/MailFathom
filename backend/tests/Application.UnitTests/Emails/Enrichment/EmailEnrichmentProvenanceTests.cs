// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Enrichment;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Enrichment;

/// <summary>Covers the record that makes a rule's verdict and a model's distinguishable in the data.</summary>
/// <remarks>
/// The two factories are what the distinction rests on: a reader asking which of them produced a mark reads the source
/// column rather than inferring it from a naming convention, which is what would happen if only one of the two could
/// ever be written.
/// </remarks>
public sealed class EmailEnrichmentProvenanceTests
{
    [Fact]
    public void FromAgent_AComposedAgentName_RecordsItAsAModelVerdict()
    {
        // Act
        var provenance = EmailEnrichmentProvenance.FromAgent("mailfathom-email-enrichment");

        // Assert
        Assert.Equal(EmailEnrichmentSource.Model, provenance.Source);
        Assert.Equal("mailfathom-email-enrichment", provenance.Origin);
    }

    [Fact]
    public void FromRule_ARuleIdentity_RecordsItAsADeterministicVerdict()
    {
        // Act
        var provenance = EmailEnrichmentProvenance.FromRule("invoice-due-date");

        // Assert
        Assert.Equal(EmailEnrichmentSource.DeterministicRule, provenance.Source);
        Assert.Equal("invoice-due-date", provenance.Origin);
    }

    /// <summary>Two marks the same producer wrote carry equal provenance, so a reader groups them by it.</summary>
    [Fact]
    public void FromAgent_TheSameNameTwice_ProducesEqualProvenance()
    {
        // Act
        var first = EmailEnrichmentProvenance.FromAgent("mailfathom-email-enrichment");
        var second = EmailEnrichmentProvenance.FromAgent("mailfathom-email-enrichment");

        // Assert
        Assert.Equal(first, second);
    }

    /// <summary>The same name under either source is a different standing, so the two never compare equal.</summary>
    [Fact]
    public void FromRule_TheSameNameAsAnAgent_DoesNotEqualTheAgentsProvenance()
    {
        // Act
        var rule = EmailEnrichmentProvenance.FromRule("shared-name");
        var agent = EmailEnrichmentProvenance.FromAgent("shared-name");

        // Assert
        Assert.NotEqual(rule, agent);
    }

    [Fact]
    public void Restore_ARecordedMark_ReadsBackWhatWasWritten()
    {
        // Act
        var provenance = EmailEnrichmentProvenance.Restore(
            EmailEnrichmentSource.DeterministicRule,
            "  invoice-due-date  ");

        // Assert
        Assert.Equal(EmailEnrichmentSource.DeterministicRule, provenance.Source);
        Assert.Equal("invoice-due-date", provenance.Origin);
    }

    [Fact]
    public void Restore_ASourceThisSystemDoesNotDeriveFrom_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EmailEnrichmentProvenance.Restore((EmailEnrichmentSource)83, "invoice-due-date"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FromAgent_ABlankName_IsRefused(string agentName)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => EmailEnrichmentProvenance.FromAgent(agentName));
    }

    /// <summary>Every origin this system writes is short, so a long one is a producer that named itself wrongly.</summary>
    [Fact]
    public void FromAgent_AnOverLongName_IsRefusedRatherThanShortened()
    {
        // Arrange
        var overLong = new string('a', EmailEnrichmentProvenance.MaximumOriginLength + 1);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => EmailEnrichmentProvenance.FromAgent(overLong));
    }

    [Fact]
    public void FromRule_ANameCarryingAControlCharacter_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => EmailEnrichmentProvenance.FromRule("invoice\u0007due-date"));
    }
}
