// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Observability;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Stands in for the spans a folder run's stages are published as, keeping which stages ran and how each ended.</summary>
/// <remarks>
/// It records the stage, that its scope was closed, and whether it reported completing, which is the whole of what a use
/// case decides about one. What those become — a span name, an outcome tag, a status — is the adapter's contract and is
/// proved against a real listener where that adapter lives.
/// </remarks>
internal sealed class RecordingMailSynchronizationPhaseTelemetry : IMailSynchronizationPhaseTelemetry
{
    private readonly List<PublishedPhase> phases = [];

    /// <summary>Gets the stages that were opened, in the order they began.</summary>
    public IReadOnlyList<PublishedPhase> Phases => this.phases;

    /// <inheritdoc />
    public IMailSynchronizationPhaseScope BeginPhase(
        MailSynchronizationPhase phase,
        CancellationToken cancellationToken)
    {
        var published = new PublishedPhase(phase);
        this.phases.Add(published);

        return published;
    }

    /// <summary>One opened stage and what it reported before its scope was closed.</summary>
    internal sealed class PublishedPhase(MailSynchronizationPhase phase) : IMailSynchronizationPhaseScope
    {
        /// <summary>Gets the stage that was opened.</summary>
        public MailSynchronizationPhase Phase => phase;

        /// <summary>Gets whether the stage reported running to its end.</summary>
        public bool WasCompleted { get; private set; }

        /// <summary>Gets whether the scope was closed, which a stage conducted inside it always is.</summary>
        public bool WasClosed { get; private set; }

        /// <inheritdoc />
        public void Completed() => this.WasCompleted = true;

        /// <inheritdoc />
        public void Dispose() => this.WasClosed = true;
    }
}
