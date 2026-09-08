// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Enrichment;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Enrichment;

/// <summary>
/// Covers the scan every read publishing a derivation runs its marks through, which is why it is asserted here rather
/// than once per read: a list and a conversation draw the same marks, and the two would otherwise be free to scan them
/// differently.
/// </summary>
public sealed class GuardedEmailEnrichmentTests
{
    private const string Marker = "AKIAEXAMPLEKEY";

    private static readonly DateTimeOffset Derived = new(2026, 9, 7, 8, 0, 0, TimeSpan.Zero);

    /// <summary>The reading and the reason are somebody's mail; the aspect, the evidence and the provenance are not.</summary>
    [Fact]
    public async Task ScanAsync_ADeploymentThatScans_ScansTheReadingAndTheReasonAndLeavesEverythingElse()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        using var acting = egress.Guard.ActingFor(ScanningSensitiveContentEgress.User);
        var mark = Mark($"a key {Marker} was pasted", $"the passage carries {Marker}");
        var enrichment = Enrichment(mark);

        // Act
        var scanned = await GuardedEmailEnrichment.ScanAsync(
            egress.Guard,
            SensitiveContentEgressPoint.ClientMailListing,
            enrichment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(scanned);

        var guarded = Assert.Single(scanned.Marks);

        Assert.DoesNotContain(Marker, guarded.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, guarded.Reason, StringComparison.Ordinal);
        Assert.Equal(mark.Aspect, guarded.Aspect);
        Assert.Equal(mark.Evidence, guarded.Evidence);
        Assert.Equal(mark.Provenance, guarded.Provenance);
        Assert.Equal(mark.DueAt, guarded.DueAt);
        Assert.Equal(Derived, scanned.DerivedAt);
    }

    /// <summary>A message no derivation has reached carries nothing to scan, and answering an empty object would be a lie.</summary>
    [Fact]
    public async Task ScanAsync_AMessageNoDerivationHasReached_AnswersNothing()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);

        // Act
        var scanned = await GuardedEmailEnrichment.ScanAsync(
            egress.Guard,
            SensitiveContentEgressPoint.ClientMailListing,
            enrichment: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(scanned);
    }

    /// <summary>A derivation that settled with nothing to say is a state a client draws, not an absence to collapse.</summary>
    [Fact]
    public async Task ScanAsync_ADerivationThatSettledWithNothingToSay_KeepsItAsItStands()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        var enrichment = Enrichment();

        // Act
        var scanned = await GuardedEmailEnrichment.ScanAsync(
            egress.Guard,
            SensitiveContentEgressPoint.ClientMailListing,
            enrichment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(scanned);
        Assert.Empty(scanned.Marks);
        Assert.Equal(Derived, scanned.DerivedAt);
    }

    private static EmailEnrichment Enrichment(params EmailEnrichmentMark[] marks) => new(
        StoredEmailId.Create(Guid.CreateVersion7()),
        marks,
        Derived);

    private static EmailEnrichmentMark Mark(string text, string reason) => EmailEnrichmentMark.Create(
        EmailEnrichmentAspect.Sense,
        text,
        reason,
        [EmailChunkId.Create(Guid.CreateVersion7())],
        EmailEnrichmentProvenance.FromAgent("mailfathom-email-enrichment"));
}
