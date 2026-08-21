// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Paging;
using MailFathom.Application.Rules.History;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules.History;

/// <summary>Covers the boundary one page of the rule history hands to the next, over its own identity.</summary>
/// <remarks>
/// The encoding is <see cref="KeysetCursorPayload" />'s and is covered once beside it, refusals included. What is
/// asserted here is this reading's own half: the recorded text a client may be part-way through, the execution identity
/// it reads back, and the boundary shape this reading has no row for.
/// </remarks>
public sealed class MailRuleExecutionCursorTests
{
    private const string Fingerprint = "abcdef0123456789";

    private const string RecordedCursor =
        "MS42MzkyMjcyODYwMDAwMDAwMDAuMDE5ODkzZTU2YWQwN2JkMDlmMTE2YzNhMWQ1ZTRiMjEuYWJjZGVmMDEyMzQ1Njc4OQ";

    private static readonly DateTimeOffset EvaluatedAt = new(2026, 8, 19, 9, 30, 0, TimeSpan.Zero);

    private static readonly MailRuleExecutionId Execution =
        MailRuleExecutionId.Create(new Guid("019893e5-6ad0-7bd0-9f11-6c3a1d5e4b21"));

    /// <summary>A client holds this text between two pages, so it is pinned rather than only compared with itself.</summary>
    [Fact]
    public void Encode_ARecordedBoundary_RoundTripsThroughTheTextThisReadingHasAlwaysIssued()
    {
        // Act
        var encoded = MailRuleExecutionCursor.After(EvaluatedAt, Execution, Fingerprint).Encode();
        var read = MailRuleExecutionCursor.TryDecode(RecordedCursor, out var cursor);

        // Assert
        Assert.Equal(RecordedCursor, encoded);
        Assert.True(read);
        Assert.NotNull(cursor);
        Assert.Equal(EvaluatedAt, cursor.Value.EvaluatedAt);
        Assert.Equal(Execution, cursor.Value.ExecutionId);
        Assert.Equal(Fingerprint, cursor.Value.FilterFingerprint);
    }

    /// <summary>Every execution this history returns was evaluated at a known instant, so a payload with none names nothing here.</summary>
    [Fact]
    public void TryDecode_APayloadCarryingNoPosition_IsRefused()
    {
        // Arrange
        var withoutPosition = KeysetCursorPayload.At(null, Execution.Value, Fingerprint).Encode();

        // Act
        var read = MailRuleExecutionCursor.TryDecode(withoutPosition, out var cursor);

        // Assert
        Assert.False(read);
        Assert.Null(cursor);
    }

    /// <summary>A cursor proves which walk it belongs to, so it is refused without the fingerprint that says so.</summary>
    [Fact]
    public void After_ABlankFingerprint_IsRefused()
    {
        // Act, Assert
        var failure = Assert.Throws<ArgumentException>(
            () => MailRuleExecutionCursor.After(EvaluatedAt, Execution, "   "));

        Assert.Equal("filterFingerprint", failure.ParamName);
    }
}
