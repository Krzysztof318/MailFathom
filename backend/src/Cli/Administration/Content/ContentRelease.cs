// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Content;

/// <summary>What one release freed, and how much of the deployment's database is still a copy of its bucket.</summary>
/// <param name="ReleasedPayloadCount">How many retained copies the request freed, which is none on a reading.</param>
/// <param name="ReleasedByteCount">How many bytes of raw MIME those copies were holding.</param>
/// <param name="RetainedPayloadCount">How many payloads still carry a copy beside their object.</param>
/// <param name="RetainedByteCount">How many bytes of raw MIME those copies hold between them.</param>
/// <param name="AwaitingMovePayloadCount">How many payloads the database still owns, which is what refuses a release.</param>
/// <remarks>
/// The same record answers a reading and a release, because they are the same three figures asked at two moments: what
/// this act did, what is left, and whether asking is permitted at all.
/// </remarks>
internal sealed record ContentReleaseReport(
    [property: JsonPropertyName("releasedPayloadCount")] long ReleasedPayloadCount,
    [property: JsonPropertyName("releasedByteCount")] long ReleasedByteCount,
    [property: JsonPropertyName("retainedPayloadCount")] long RetainedPayloadCount,
    [property: JsonPropertyName("retainedByteCount")] long RetainedByteCount,
    [property: JsonPropertyName("awaitingMovePayloadCount")] long AwaitingMovePayloadCount)
{
    /// <summary>Gets whether a further release would have anything left to free.</summary>
    internal bool PayloadsRemain => this.RetainedPayloadCount > 0;

    /// <summary>Describes what is still duplicated, in counts and volume rather than in an estimate of time.</summary>
    /// <returns>The retained figures, grouped invariantly for the reason every other figure this tool prints is.</returns>
    internal string DescribeRetained() => Describe(this.RetainedPayloadCount, this.RetainedByteCount);

    private static string Describe(long payloadCount, long byteCount) => string.Create(
        CultureInfo.InvariantCulture,
        $"{payloadCount:N0} payloads carrying {byteCount:N0} bytes");
}
