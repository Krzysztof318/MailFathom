// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Retrieval.AskMail;

/// <summary>Publishes one answering run as the operation somebody asked for, beside the request it happened inside.</summary>
/// <remarks>
/// <para>
/// The half of the record that is not durable, and the half that answers a different question. The durable entry says
/// which messages a run read, months later and on a deployment exporting nothing; this says how long a run took, how
/// much it considered, and how it ended, correlated with the tool call it happened inside — which is what makes
/// diagnosing this feature the same act as diagnosing everything else the process does.
/// </para>
/// <para>
/// A port rather than a call into a tracing API, because starting a span is infrastructure: the application states that
/// a run began and ended and what it did, and an adapter decides which registry that reaches. It is also what keeps the
/// signal's privacy rule in one place, since nothing above the adapter can attach a tag to it.
/// </para>
/// </remarks>
public interface IMailAnsweringRunTelemetry
{
    /// <summary>Opens the report of one run, and publishes it when the returned scope is disposed.</summary>
    /// <param name="observation">The run's own record, read as the scope is disposed rather than when it is opened.</param>
    /// <returns>The scope, which the caller must dispose exactly once and which the run must be conducted inside.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="observation" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The observation is read at the end rather than passed at the end, because the report has to be open <em>around</em>
    /// the run for the calls the run makes to be reported beneath it. A run that threw is therefore reported exactly as
    /// one that answered, with whatever it managed to do before it ended.
    /// </remarks>
    IDisposable BeginRun(MailAnsweringRunObservation observation);
}
