// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.DeadLetters;
using MailFathom.Application.Paging;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs.DeadLetters;

/// <summary>Covers the boundary a continued reading of the dead letters resumes from, over its own identity.</summary>
/// <remarks>
/// The encoding is <see cref="KeysetCursorPayload" />'s and is covered once beside it, refusals included. What is
/// asserted here is this reading's own half: the recorded text a client may be part-way through, the job identity it
/// reads back, and the boundary shape this reading has no row for.
/// </remarks>
public sealed class DeadLetteredJobCursorTests
{
    private const string Fingerprint = "abcdef0123456789";

    private const string RecordedCursor =
        "MS42MzkyMjcyODYwMDAwMDAwMDAuMDE5ODkzZTU2YWQwN2JkMDlmMTE2YzNhMWQ1ZTRiMjMuYWJjZGVmMDEyMzQ1Njc4OQ";

    private static readonly DateTimeOffset StoppedAt = new(2026, 8, 19, 9, 30, 0, TimeSpan.Zero);

    private static readonly JobId Job = JobId.Create(new Guid("019893e5-6ad0-7bd0-9f11-6c3a1d5e4b23"));

    /// <summary>A client holds this text between two pages, so it is pinned rather than only compared with itself.</summary>
    [Fact]
    public void Encode_ARecordedBoundary_RoundTripsThroughTheTextThisReadingHasAlwaysIssued()
    {
        // Act
        var encoded = DeadLetteredJobCursor.After(StoppedAt, Job, Fingerprint).Encode();
        var read = DeadLetteredJobCursor.TryDecode(RecordedCursor, out var cursor);

        // Assert
        Assert.Equal(RecordedCursor, encoded);
        Assert.True(read);
        Assert.NotNull(cursor);
        Assert.Equal(StoppedAt, cursor.Value.DeadLetteredAt);
        Assert.Equal(Job, cursor.Value.JobId);
        Assert.Equal(Fingerprint, cursor.Value.FilterFingerprint);
    }

    /// <summary>Every job this reading returns stopped at a known instant, so a payload with none names nothing here.</summary>
    [Fact]
    public void TryDecode_APayloadCarryingNoPosition_IsRefused()
    {
        // Arrange
        var withoutPosition = KeysetCursorPayload.At(null, Job.Value, Fingerprint).Encode();

        // Act
        var read = DeadLetteredJobCursor.TryDecode(withoutPosition, out var cursor);

        // Assert
        Assert.False(read);
        Assert.Null(cursor);
    }

    /// <summary>A cursor proves which walk it belongs to, so it is refused without the fingerprint that says so.</summary>
    [Fact]
    public void After_ABlankFingerprint_IsRefused()
    {
        // Act, Assert
        var failure = Assert.Throws<ArgumentException>(() => DeadLetteredJobCursor.After(StoppedAt, Job, "   "));

        Assert.Equal("filterFingerprint", failure.ParamName);
    }
}
