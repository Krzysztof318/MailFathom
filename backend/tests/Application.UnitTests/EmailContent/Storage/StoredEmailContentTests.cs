// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using Xunit;

namespace MailFathom.Application.UnitTests.EmailContent.Storage;

/// <summary>Covers how a stored payload is checked against what was recorded for it when it was written.</summary>
public sealed class StoredEmailContentTests
{
    private static readonly byte[] RawMime = Encoding.UTF8.GetBytes("From: sender@example.test\r\n\r\nBody");

    /// <summary>A payload that is what the writer stored has no defect, which is the ordinary case.</summary>
    [Fact]
    public void FindIntegrityDefect_PayloadMatchingItsRecordedLengthAndDigest_ReportsNoDefect()
    {
        // Arrange
        var content = new StoredEmailContent(RawMime, RawMime.Length, SHA256.HashData(RawMime));

        // Act
        var defect = content.FindIntegrityDefect();

        // Assert
        Assert.Null(defect);
    }

    /// <summary>A payload shorter or longer than recorded is a length fault, which is what a partial write leaves.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void FindIntegrityDefect_PayloadOfADifferentLengthThanRecorded_ReportsTheLengthMismatch(int lengthDifference)
    {
        // Arrange
        var content = new StoredEmailContent(
            RawMime,
            RawMime.Length + lengthDifference,
            SHA256.HashData(RawMime));

        // Act
        var defect = content.FindIntegrityDefect();

        // Assert
        Assert.Equal(EmailContentDefect.ByteLengthMismatch, defect);
    }

    /// <summary>A payload of the right length whose bytes changed is a digest fault, not a length one.</summary>
    [Fact]
    public void FindIntegrityDefect_PayloadOfTheRecordedLengthWithChangedBytes_ReportsTheHashMismatch()
    {
        // Arrange
        var changedPayload = RawMime.ToArray();
        changedPayload[^1] ^= 0xFF;
        var content = new StoredEmailContent(changedPayload, RawMime.Length, SHA256.HashData(RawMime));

        // Act
        var defect = content.FindIntegrityDefect();

        // Assert
        Assert.Equal(EmailContentDefect.HashMismatch, defect);
    }

    /// <summary>
    /// A payload the store already checked answers from that answer rather than hashing the whole message again, which
    /// is what keeps a mailbox read paying for one digest instead of one per reader.
    /// </summary>
    [Fact]
    public void FindIntegrityDefect_PayloadTheStoreAlreadyVerified_AnswersWithoutCheckingItAgain()
    {
        // Arrange: a length nothing could match, so anything but the recorded answer is a check that ran.
        var content = new StoredEmailContent(RawMime, RawMime.Length + 1, SHA256.HashData(RawMime))
        {
            WasVerifiedIntact = true,
        };

        // Act
        var defect = content.FindIntegrityDefect();

        // Assert
        Assert.Null(defect);
    }

    /// <summary>A digest of the wrong size is a mismatch rather than a comparison that throws.</summary>
    [Fact]
    public void FindIntegrityDefect_RecordedDigestOfTheWrongLength_ReportsTheHashMismatch()
    {
        // Arrange
        var content = new StoredEmailContent(RawMime, RawMime.Length, new byte[] { 0x01, 0x02 });

        // Act
        var defect = content.FindIntegrityDefect();

        // Assert
        Assert.Equal(EmailContentDefect.HashMismatch, defect);
    }
}
