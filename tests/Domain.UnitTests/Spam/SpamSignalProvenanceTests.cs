// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Spam;
using Xunit;

namespace MailFathom.Domain.UnitTests.Spam;

public sealed class SpamSignalProvenanceTests
{
    [Fact]
    public void FromMessageHeader_AFieldName_RecordsItAgainstTheHeaderSource()
    {
        // Arrange, Act
        var provenance = SpamSignalProvenance.FromMessageHeader("  Authentication-Results  ");

        // Assert
        Assert.Equal(SpamSignalSource.MessageHeader, provenance.Source);
        Assert.Equal("Authentication-Results", provenance.Origin);
    }

    [Fact]
    public void FromFolderPlacement_AnAlias_RecordsItAgainstTheFolderSource()
    {
        // Arrange, Act
        var provenance = SpamSignalProvenance.FromFolderPlacement("JUNK");

        // Assert
        Assert.Equal(SpamSignalSource.FolderPlacement, provenance.Source);
        Assert.Equal("JUNK", provenance.Origin);
    }

    [Fact]
    public void FromScannerCorpus_ARevision_RecordsItAgainstTheCorpusSource()
    {
        // Arrange, Act
        var provenance = SpamSignalProvenance.FromScannerCorpus("4.0.2");

        // Assert
        Assert.Equal(SpamSignalSource.ScannerCorpus, provenance.Source);
        Assert.Equal("4.0.2", provenance.Origin);
    }

    [Fact]
    public void Restore_ARecordedPair_ComparesEqualToTheOneItWasBuiltFrom()
    {
        // Arrange
        var recorded = SpamSignalProvenance.FromMessageHeader("X-Spam-Status");

        // Act
        var restored = SpamSignalProvenance.Restore(recorded.Source, recorded.Origin);

        // Assert
        Assert.Equal(recorded, restored);
    }

    [Fact]
    public void Restore_ASourceOutsideTheDeclaredSet_IsRefused()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SpamSignalProvenance.Restore((SpamSignalSource)9, "X-Spam-Flag"));
    }

    [Fact]
    public void FromMessageHeader_AnOriginLongerThanTheBound_IsRefusedRatherThanShortened()
    {
        // Arrange
        var written = new string('h', SpamSignalProvenance.MaximumOriginLength + 1);

        // Act, Assert
        var failure = Assert.Throws<ArgumentException>(() => SpamSignalProvenance.FromMessageHeader(written));

        Assert.Equal("headerFieldName", failure.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromScannerCorpus_ARevisionHoldingNothing_IsRefused(string? revision)
    {
        // Arrange, Act, Assert
        Assert.ThrowsAny<ArgumentException>(() => SpamSignalProvenance.FromScannerCorpus(revision!));
    }
}
