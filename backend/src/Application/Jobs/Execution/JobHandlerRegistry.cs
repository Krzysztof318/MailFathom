// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Application.Jobs.Execution;

/// <summary>Answers which job types this process can run, and which handler runs each of them.</summary>
/// <remarks>
/// <para>
/// It is asked twice for every pass and for two different reasons. The claim is filtered to the types named here, so a
/// job a deployment cannot run is left where it is rather than taken and abandoned; and dispatch then asks again for
/// the one job it holds, because the two questions are answered at different moments and a type registered for neither
/// would otherwise reach the work.
/// </para>
/// <para>
/// A process with no handler at all is the ordinary state of a build whose consumers have not arrived yet, so an empty
/// registry is valid and the worker's answer to it is to claim nothing.
/// </para>
/// </remarks>
public sealed class JobHandlerRegistry
{
    private readonly Dictionary<JobType, IJobHandler> handlersByType;

    /// <summary>Indexes the handlers this process registered.</summary>
    /// <param name="handlers">Every handler the composition root supplied.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="handlers" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when a handler names the unspecified job type, or when two handlers name one type.</exception>
    /// <remarks>
    /// Two handlers for one type are refused rather than resolved by order, because either could be the one meant and
    /// choosing silently would make which one runs depend on registration order. It is a defect in how the process was
    /// composed rather than a condition a run can recover from, so the refusal is raised where it is noticed rather
    /// than absorbed into an outcome.
    /// </remarks>
    public JobHandlerRegistry(IEnumerable<IJobHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);

        var registeredHandlers = handlers.ToArray();

        if (registeredHandlers.Any(handler => !handler.JobType.IsSpecified))
        {
            throw new ArgumentException("A job handler names a declared job type.", nameof(handlers));
        }

        var duplicatedType = registeredHandlers
            .GroupBy(handler => handler.JobType)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicatedType is not null)
        {
            throw new ArgumentException(
                $"Two job handlers are registered for '{duplicatedType.Key.Name}', and only one may run it.",
                nameof(handlers));
        }

        this.handlersByType = registeredHandlers.ToDictionary(handler => handler.JobType);
        this.HandledTypes = [.. this.handlersByType.Keys.OrderBy(jobType => jobType.Name, StringComparer.Ordinal)];
    }

    /// <summary>Gets the job types this process has a handler for, in a stable order.</summary>
    public IReadOnlyList<JobType> HandledTypes { get; }

    /// <summary>Finds the handler that runs one job type.</summary>
    /// <param name="jobType">The type to dispatch.</param>
    /// <param name="handler">The handler for that type, when one is registered.</param>
    /// <returns><see langword="true" /> when this process can run the type; otherwise <see langword="false" />.</returns>
    public bool TryGetHandler(JobType jobType, [NotNullWhen(true)] out IJobHandler? handler) =>
        this.handlersByType.TryGetValue(jobType, out handler);
}
