// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Observability;

/// <summary>What answering has cost so far in the period currently running, as this instance last read it.</summary>
/// <param name="PeriodStartedAt">When the current period began, so a reader can tell how much of it is left.</param>
/// <param name="Runs">How many runs the period has admitted, across every replica.</param>
/// <param name="Tokens">The tokens those runs have consumed, sent and received together.</param>
/// <remarks>
/// <para>
/// Counts and a moment, and deliberately nothing else. This is what makes a deployment's answering spend observable
/// while it is being spent rather than at the end of a billing period, and it says nothing about what was asked, what
/// was answered, or which mail was read — a size describes the shape of what left without describing any of it.
/// </para>
/// <para>
/// Both figures are the deployment's rather than this process's, because the ledger they come from is. What is this
/// process's is only how recently it read them: they are refreshed by the write every admission and every spend
/// already makes, so an instance answering questions publishes current figures and one answering none publishes what
/// it last saw.
/// </para>
/// <para>
/// It sits with the tracker that reports it rather than with the ledger port, because nothing above that boundary acts
/// on the figure: a use case is told whether a question may run and never how close the period is to its ceiling.
/// </para>
/// </remarks>
public sealed record MailAnsweringSpend(DateTimeOffset PeriodStartedAt, int Runs, long Tokens);
