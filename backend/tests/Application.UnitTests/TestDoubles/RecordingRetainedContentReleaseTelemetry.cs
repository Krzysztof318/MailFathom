// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Observability;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Keeps what each release published, so a request that freed nothing can be told from one that published a zero.</summary>
internal sealed class RecordingRetainedContentReleaseTelemetry : IRetainedContentReleaseTelemetry
{
    private readonly List<(long PayloadCount, long ByteCount)> releases = [];

    /// <summary>Gets what each published release reported freeing.</summary>
    internal IReadOnlyList<(long PayloadCount, long ByteCount)> Releases => this.releases;

    /// <inheritdoc />
    public void Released(long payloadCount, long byteCount) => this.releases.Add((payloadCount, byteCount));
}
