// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Observability;

/// <summary>Publishes the stages of one folder run beneath the span the run itself is reported as.</summary>
/// <remarks>
/// <para>
/// The folder run's own span is opened where the run is scheduled, and everything this reports sits inside it. That
/// nesting is the whole point: a run whose duration doubled is attributable to the stage it doubled in — the mail
/// server, the local derivation, the reconciliation pass — rather than to the folder as a whole, which is the same
/// failure the account and folder pair was introduced to fix, one level down.
/// </para>
/// <para>
/// A port rather than a call into a tracing API, for the reason the mailbox read's is: starting a span is
/// infrastructure, and the use case states that a stage began and that it is over. Nothing above the adapter can attach
/// a tag, so a stage reports the work it is and nothing about the mail it passed over.
/// </para>
/// </remarks>
public interface IMailSynchronizationPhaseTelemetry
{
    /// <summary>Opens the report of one stage of a folder run, and publishes it when the returned scope is disposed.</summary>
    /// <param name="phase">Which stage is beginning.</param>
    /// <param name="cancellationToken">The run's token, read as the scope is disposed to tell shutdown from a failure.</param>
    /// <returns>The scope, which the caller must dispose exactly once and which the stage must be conducted inside.</returns>
    IMailSynchronizationPhaseScope BeginPhase(MailSynchronizationPhase phase, CancellationToken cancellationToken);
}
