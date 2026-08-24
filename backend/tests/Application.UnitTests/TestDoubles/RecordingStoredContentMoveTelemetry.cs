// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Move;
using MailFathom.Application.Observability;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Keeps what a pass published, so a refusal's reason can be asserted rather than inferred from a count.</summary>
internal sealed class RecordingStoredContentMoveTelemetry : IStoredContentMoveTelemetry
{
    private readonly List<long> copiedByteLengths = [];
    private readonly List<StoredContentMoveFailure> failures = [];

    /// <summary>Gets the byte length of every payload reported as moved.</summary>
    internal IReadOnlyList<long> CopiedByteLengths => this.copiedByteLengths;

    /// <summary>Gets why each refused payload was left in the database.</summary>
    internal IReadOnlyList<StoredContentMoveFailure> Failures => this.failures;

    /// <summary>Gets how many passes were opened, which is what proves an idle move opened none.</summary>
    internal int PassCount { get; private set; }

    /// <summary>Gets whether a pass reported reaching the end of the content.</summary>
    internal bool ReachedEndOfContent { get; private set; }

    /// <inheritdoc />
    public IStoredContentMovePassScope BeginPass()
    {
        this.PassCount++;

        return new PassScope(this);
    }

    private sealed class PassScope(RecordingStoredContentMoveTelemetry telemetry) : IStoredContentMovePassScope
    {
        public void Copied(long byteLength) => telemetry.copiedByteLengths.Add(byteLength);

        public void Failed(StoredContentMoveFailure failure) => telemetry.failures.Add(failure);

        public void ReachedEndOfContent() => telemetry.ReachedEndOfContent = true;

        public void Dispose()
        {
            // Nothing is measured here, so there is nothing to publish when the pass ends.
        }
    }
}
