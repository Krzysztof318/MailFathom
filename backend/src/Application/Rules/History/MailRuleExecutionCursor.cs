// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Paging;

namespace MailFathom.Application.Rules.History;

/// <summary>Marks where one page of the rule history ended, so the next page continues from it.</summary>
/// <remarks>
/// <para>
/// The history is ordered newest first by evaluation instant, with the execution identifier breaking a tie, and this
/// pairs those two values with a fingerprint of the filters the page was read under. The pair is what makes pagination
/// keyset-based rather than offset-based: a pass that records executions between two requests neither shifts a window
/// nor causes an execution to be skipped or repeated. The fingerprint is what makes the boundary meaningful, because a
/// position names a page edge only within the filtered set it was computed for.
/// </para>
/// <para>
/// It carries no secret and needs no signature: every value in it is one the caller already supplied or already
/// received. Encoding is about opacity rather than protection — a client that cannot read a cursor does not build one.
/// The encoded form itself is <see cref="KeysetCursorPayload" />'s, which every keyset cursor here shares.
/// </para>
/// </remarks>
public readonly record struct MailRuleExecutionCursor
{
    private MailRuleExecutionCursor(
        DateTimeOffset evaluatedAt,
        MailRuleExecutionId executionId,
        string filterFingerprint)
    {
        this.EvaluatedAt = evaluatedAt;
        this.ExecutionId = executionId;
        this.FilterFingerprint = filterFingerprint;
    }

    /// <summary>Gets the evaluation instant of the last execution the page returned.</summary>
    public DateTimeOffset EvaluatedAt { get; }

    /// <summary>Gets the identity of that execution, which breaks a tie between two recorded in one instant.</summary>
    public MailRuleExecutionId ExecutionId { get; }

    /// <summary>Gets the fingerprint of the filters this cursor was issued for.</summary>
    public string FilterFingerprint { get; }

    /// <summary>Creates the cursor that continues a walk after one position in the history.</summary>
    /// <param name="evaluatedAt">The evaluation instant the page ended on.</param>
    /// <param name="executionId">The identity of the execution at that instant.</param>
    /// <param name="filterFingerprint">The fingerprint of the filters the page was read under.</param>
    /// <returns>The cursor.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="filterFingerprint" /> is blank.</exception>
    public static MailRuleExecutionCursor After(
        DateTimeOffset evaluatedAt,
        MailRuleExecutionId executionId,
        string filterFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filterFingerprint);

        return new MailRuleExecutionCursor(evaluatedAt, executionId, filterFingerprint);
    }

    /// <summary>Reads a cursor a caller presented.</summary>
    /// <param name="text">The encoded cursor, as a previous page returned it.</param>
    /// <param name="cursor">The decoded cursor when the text is one this version issued; otherwise <see langword="null" />.</param>
    /// <returns><see langword="true" /> when the text decoded into a usable cursor; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// Every execution this history returns was evaluated at a known instant, so a payload carrying none names no
    /// boundary here and is refused. Whether a decoded cursor belongs to the current request is a separate question its
    /// <see cref="FilterFingerprint" /> answers, and one this method deliberately does not ask.
    /// </remarks>
    public static bool TryDecode(string? text, out MailRuleExecutionCursor? cursor)
    {
        cursor = null;

        if (!KeysetCursorPayload.TryDecode(text, out var payload) || payload.Position is not { } evaluatedAt)
        {
            return false;
        }

        cursor = new MailRuleExecutionCursor(
            evaluatedAt,
            MailRuleExecutionId.Create(payload.Identity),
            payload.FilterFingerprint);

        return true;
    }

    /// <summary>Writes the cursor as the opaque string a caller presents to continue the walk.</summary>
    /// <returns>The encoded cursor.</returns>
    public string Encode() =>
        KeysetCursorPayload.At(this.EvaluatedAt, this.ExecutionId.Value, this.FilterFingerprint).Encode();
}
