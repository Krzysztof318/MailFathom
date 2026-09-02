// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Common.Observability;
using MailFathom.Infrastructure.ObjectStorage;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Publishes what reaching the object-storage endpoint costs, and what stops it when it fails.</summary>
/// <remarks>
/// <para>
/// Three questions an operator asks of a second content backend, and one instrument each: how much of it is happening,
/// how long it takes, and how much is moving over the wire. The fourth — why it is failing — is a dimension rather than
/// an instrument, because a refused credential and an unreachable endpoint are the same operation ending differently and
/// an operator wants them in one series they can split.
/// </para>
/// <para>
/// Durations and sizes are distributions rather than totals, because what is acted on here is the tail: one enormous
/// message and a steady stream of ordinary ones cost the same in a sum and mean entirely different things.
/// </para>
/// <para>
/// <b>Nothing here carries an object key, a bucket, an endpoint address, or any part of a payload.</b> A key names the
/// row that owns it and therefore a message, and the payload is mail; the operation and the classification are
/// MailFathom's own words and are the whole of what is published.
/// </para>
/// </remarks>
internal sealed class ObjectStorageTelemetry
{
    /// <summary>Names the operation an object-storage measurement belongs to.</summary>
    internal const string OperationTagName = "mailfathom.object_storage.operation";

    /// <summary>Names how an operation ended.</summary>
    internal const string OutcomeTagName = "mailfathom.object_storage.outcome";

    /// <summary>Names what ended an operation that failed.</summary>
    internal const string FailureTagName = "mailfathom.object_storage.failure";

    /// <summary>Names an operation the endpoint answered.</summary>
    internal const string SucceededOutcomeName = "succeeded";

    /// <summary>Names an operation that ended in a classified failure, including one the caller abandoned.</summary>
    internal const string FailedOutcomeName = "failed";

    /// <summary>Names listing the keys beneath this deployment's prefix.</summary>
    internal const string ListOperationName = "list";

    /// <summary>Names writing one object.</summary>
    internal const string PutOperationName = "put";

    /// <summary>Names reading one object back.</summary>
    internal const string GetOperationName = "get";

    /// <summary>Names removing one object.</summary>
    internal const string DeleteOperationName = "delete";

    private readonly TimeProvider timeProvider;
    private readonly Counter<long> operations;
    private readonly Histogram<double> operationDuration;
    private readonly Histogram<long> transferredBytes;

    /// <summary>Initializes the instruments every object-storage operation is published through.</summary>
    /// <param name="timeProvider">Measures how long one operation took.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    public ObjectStorageTelemetry(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.timeProvider = timeProvider;

        this.operations = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.object_storage.operations",
            unit: "{operation}",
            description: "How many operations were made against the object-storage endpoint, by operation and by how each ended.");
        this.operationDuration = Telemetry.Meter.CreateHistogram<double>(
            "mailfathom.object_storage.operation.duration",
            unit: "s",
            description: "How long one operation against the object-storage endpoint took, by operation and by how it ended.");
        this.transferredBytes = Telemetry.Meter.CreateHistogram<long>(
            "mailfathom.object_storage.bytes",
            unit: "By",
            description: "How many payload bytes one operation against the object-storage endpoint carried.");
    }

    /// <summary>Begins measuring one operation, and returns the scope that ends it.</summary>
    /// <param name="operation">Which operation it is, named by one of this type's operation constants.</param>
    /// <returns>The scope, which the caller must dispose after recording how the operation ended.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="operation" /> is <see langword="null" />, empty, or whitespace.</exception>
    public OperationScope Begin(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        return new OperationScope(this, operation, this.timeProvider.GetTimestamp());
    }

    private void Record(string operation, ObjectStorageFailure failure, long? byteLength, TimeSpan elapsed)
    {
        var tags = new TagList { { OperationTagName, operation } };

        if (failure.IsSpecified)
        {
            tags.Add(OutcomeTagName, FailedOutcomeName);
            tags.Add(FailureTagName, failure.Name);
        }
        else
        {
            tags.Add(OutcomeTagName, SucceededOutcomeName);
        }

        this.operations.Add(1, tags);
        this.operationDuration.Record(elapsed.TotalSeconds, tags);

        if (byteLength is { } bytes)
        {
            this.transferredBytes.Record(bytes, new TagList { { OperationTagName, operation } });
        }
    }

    /// <summary>Carries one operation from the moment it began to how it ended.</summary>
    /// <remarks>
    /// A scope that recorded neither outcome is one that threw past every classification, which is published as an
    /// unrecognized failure rather than as a success nobody observed.
    /// </remarks>
    internal sealed class OperationScope : IDisposable
    {
        private readonly ObjectStorageTelemetry telemetry;
        private readonly string operation;
        private readonly long startingTimestamp;

        private ObjectStorageFailure failure = ObjectStorageFailure.Unrecognized;
        private long? byteLength;
        private bool ended;

        internal OperationScope(ObjectStorageTelemetry telemetry, string operation, long startingTimestamp)
        {
            this.telemetry = telemetry;
            this.operation = operation;
            this.startingTimestamp = startingTimestamp;
        }

        /// <summary>Records an operation the endpoint answered.</summary>
        /// <param name="payloadByteLength">How many payload bytes it carried, or <see langword="null" /> when it carried none.</param>
        public void Succeeded(long? payloadByteLength = null)
        {
            this.failure = default;
            this.byteLength = payloadByteLength;
        }

        /// <summary>Records an operation that ended in a classified failure.</summary>
        /// <param name="classification">What ended it.</param>
        public void Failed(ObjectStorageFailure classification) => this.failure = classification.IsSpecified
            ? classification
            : ObjectStorageFailure.Unrecognized;

        /// <inheritdoc />
        public void Dispose()
        {
            if (this.ended)
            {
                return;
            }

            this.ended = true;

            this.telemetry.Record(
                this.operation,
                this.failure,
                this.byteLength,
                this.telemetry.timeProvider.GetElapsedTime(this.startingTimestamp));
        }
    }
}
