// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Observability;

/// <summary>Publishes one read of the local mailbox copy as the operation somebody asked for.</summary>
/// <remarks>
/// <para>
/// A protocol call and the database commands it issues are both instrumented by libraries, and neither says which use
/// case ran between them. That is the gap this port closes: a read reported here sits inside the span the protocol
/// boundary already produces and parents the persistence and content-store work it causes, so a slow tool call is
/// attributable to a use case, and the use case's own cost is separable from the queries beneath it.
/// </para>
/// <para>
/// A port rather than a call into a tracing API, because starting a span is infrastructure: the application states that
/// a read began, what it returned, and that it is over, and an adapter decides which registry that reaches. It is also
/// what keeps the signal's privacy rule in one place, since nothing above the adapter can attach a tag to it — a read
/// reports counts and nothing about what was asked for or found.
/// </para>
/// </remarks>
public interface IMailboxReadTelemetry
{
    /// <summary>Opens the report of one read, and publishes it when the returned scope is disposed.</summary>
    /// <param name="operation">Which read is beginning.</param>
    /// <param name="cancellationToken">The caller's token, read as the scope is disposed to tell a caller that walked away from a read that broke.</param>
    /// <returns>The scope, which the caller must dispose exactly once and which the read must be conducted inside.</returns>
    IMailboxReadScope BeginRead(MailboxReadOperation operation, CancellationToken cancellationToken);

    /// <summary>Opens the report of the ranking one search runs, beneath the span of the search itself.</summary>
    /// <param name="cancellationToken">The caller's token, read as the scope is disposed to tell a caller that walked away from a ranking that broke.</param>
    /// <returns>The scope, which the caller must dispose exactly once and which the ranking must be conducted inside.</returns>
    /// <remarks>
    /// <para>
    /// The ends of a ranking are already spanned by the libraries they run through — the provider call by the AI
    /// instrumentation, the queries by the database one — and neither says what the ranking as a whole cost. That is
    /// the gap this closes: a search that spent its time in neither the model nor the database is still attributable,
    /// and the fusion of two rankings becomes measurable rather than being inferred from the difference.
    /// </para>
    /// <para>
    /// The count this scope is completed with is deliberately not the one
    /// <see cref="IMailboxReadScope.Completed(int)" /> documents for a read. A read reports what it returned; a ranking
    /// reports what it scored, which is deeper than the window by the depth a fusion needs. Reporting the window here
    /// would publish a number that never differs from the one on the read above, and the pair is the whole point:
    /// side by side they separate a slow fusion from a wide one.
    /// </para>
    /// </remarks>
    IMailboxReadScope BeginSearchRanking(CancellationToken cancellationToken);
}
