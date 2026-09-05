// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Signals;

namespace MailFathom.TestSupport;

/// <summary>A channel that keeps what it was handed, for a test whose subject is what a deployment says changed.</summary>
/// <remarks>Deliberately not a substitute: what such a test asserts is the sequence of signals and what each one carries, which reads as a list rather than as a set of verified calls.</remarks>
public sealed class RecordingClientSignalChannel : IClientSignalChannel
{
    private readonly List<ClientSignal> published = [];
    private readonly Lock gate = new();

    /// <summary>Gets what this channel has been handed, in the order it was handed them.</summary>
    public IReadOnlyList<ClientSignal> Published
    {
        get
        {
            lock (this.gate)
            {
                return [.. this.published];
            }
        }
    }

    /// <inheritdoc />
    public Task PublishAsync(ClientSignal signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);

        lock (this.gate)
        {
            this.published.Add(signal);
        }

        return Task.CompletedTask;
    }
}
