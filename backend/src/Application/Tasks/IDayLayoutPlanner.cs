// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Tasks;

/// <summary>Arranges one day's tasks against what is already on it.</summary>
/// <remarks>
/// <para>
/// The one way a day is laid out. An implementation is registered for every deployment, including one that declared no
/// model at all — the answer a caller needs is a reason it can act on, and a missing registration would have made the
/// use case carry a null check and decide for itself what an absence meant.
/// </para>
/// <para>
/// It runs when somebody asks and never otherwise. Nothing here is reached by a pass, a schedule, or an arrival, so a
/// deployment spends on a layout exactly as often as a person presses the control, and a day nobody asked about costs
/// nothing.
/// </para>
/// <para>
/// It never throws for a provider that failed. Every way an arrangement can come to nothing is a member of
/// <see cref="DayLayoutWithholding" />, because what the screen does with each is the same shape — say so and leave the
/// control where it was. Caller cancellation is the exception and propagates, being the person withdrawing the request
/// rather than anything about their day.
/// </para>
/// </remarks>
public interface IDayLayoutPlanner
{
    /// <summary>Gets whether this deployment lays a day out at all.</summary>
    /// <remarks>
    /// Asked so the use case answers without reading anybody's tasks or calendar on a deployment that could not use
    /// them. It is the one place the answer is given, rather than a second reading of the configuration beside the
    /// registration that already decided it.
    /// </remarks>
    bool IsActive { get; }

    /// <summary>Suggests an arrangement of one day.</summary>
    /// <param name="question">The day, what is owed on it, and what it is already committed to.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The arrangement, or the reason none was produced this time.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="question" /> is <see langword="null" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// The arrangement names only tasks the question carried, and each of them at most once. Nothing is written by
    /// producing one: what comes back is offered to the person and applied by their own acts or not at all.
    /// </remarks>
    Task<DayLayoutDerivation> SuggestAsync(DayLayoutQuestion question, CancellationToken cancellationToken);
}
