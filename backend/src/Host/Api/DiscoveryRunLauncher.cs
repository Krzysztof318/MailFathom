// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;
using MailFathom.Application.Discovery.Streaming;
using MailFathom.Application.Retrieval;
using MailFathom.Host.Security.Transport;

namespace MailFathom.Host.Api;

/// <summary>Runs one Discover run past the request that asked for it.</summary>
/// <remarks>
/// <para>
/// A streamed run is answered in two connections — one that asks the question and one, or several, that read the
/// answer — so the work belongs to neither. This puts it on a scope of its own that lives as long as the run, and it is
/// in the composition root because that is where a scope is made and where a caller is stated onto one.
/// </para>
/// <para>
/// <strong>The caller travels with the run.</strong> The request's own scope is gone by the time the run executes, so
/// the principal the transport admitted is stated onto the run's scope rather than inherited: the use case reads the
/// grant and the owner from it exactly as it would inside a request, and a run therefore refuses in the background
/// whatever it would have refused in the foreground.
/// </para>
/// <para>
/// What this bounds is the process stopping, because a run holds a database connection and there is nobody left to
/// answer. The longest a run may take is the run's own and is applied inside it, so the two endings stay distinguishable
/// to the client. Neither is the client's connection: a person who closed the page does not stop a run, because they may
/// reattach to it.
/// </para>
/// <para>
/// <strong>It is also where a run's unnamed failure is recorded.</strong> The use case publishes every ending it can
/// name and lets the rest propagate, because <c>Application</c> holds no logger and a failure nothing writes down is one
/// <see cref="DiscoveryRunFailure.Failed" /> promises an operator can read. Here there is a logger, so the fault is
/// logged and the run is ended on it.
/// </para>
/// </remarks>
internal sealed partial class DiscoveryRunLauncher
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly DiscoveryRunRegistry registry;
    private readonly IHostApplicationLifetime lifetime;
    private readonly ILogger<DiscoveryRunLauncher> logger;

    /// <summary>Initializes the launcher.</summary>
    /// <param name="scopeFactory">Makes the scope one run executes in.</param>
    /// <param name="registry">Holds the run while it executes and while a client can still come back for it.</param>
    /// <param name="lifetime">Reports that the process is stopping, which ends every run it is holding.</param>
    /// <param name="logger">Reports the failures a run cannot name to its client, which nothing else would.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public DiscoveryRunLauncher(
        IServiceScopeFactory scopeFactory,
        DiscoveryRunRegistry registry,
        IHostApplicationLifetime lifetime,
        ILogger<DiscoveryRunLauncher> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(logger);

        this.scopeFactory = scopeFactory;
        this.registry = registry;
        this.lifetime = lifetime;
        this.logger = logger;
    }

    /// <summary>Starts a run and returns without waiting for it.</summary>
    /// <param name="question">The question and the resolved scope bounding what may be read to answer it.</param>
    /// <param name="journal">Where the run publishes, which the caller has already registered.</param>
    /// <param name="caller">The principal the transport admitted, which the run executes under.</param>
    /// <returns>The task this process finishes with the run on, which the request that asked the question ignores.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The route awaits nothing and observes nothing, because the run reports itself: every ending it can reach is
    /// published to its own stream, so a task result would carry what the client has already been told. What the task
    /// does say is when this process has finished with the run — the ending is published a moment before the registry is
    /// told — which is why it is handed back rather than discarded here.
    /// </remarks>
    internal Task Start(MailQuestion question, DiscoveryRunJournal journal, AuthorizedPrincipal caller)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(caller);

        return Task.Run(() => this.ExecuteAsync(question, journal, caller));
    }

    /// <summary>Executes one run on a scope of its own and leaves it ended however it went.</summary>
    /// <remarks>
    /// The handler here is for the two endings the run cannot state to its client: a scope this process could not
    /// compose the use case out of, and a fault the use case has no name for. Both are the deployment's rather than the
    /// question's, both are what <see cref="DiscoveryRunFailure.Failed" /> stands for, and nothing else would report
    /// either — nothing awaits this task, so an exception escaping here would be observed by nobody and would leave a
    /// client watching a run that never ends.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Nothing awaits this task, so an escaping exception would be observed by nobody and would leave the run unended; it is logged and the run is ended instead.")]
    private async Task ExecuteAsync(MailQuestion question, DiscoveryRunJournal journal, AuthorizedPrincipal caller)
    {
        try
        {
            await using var scope = this.scopeFactory.CreateAsyncScope();

            scope.ServiceProvider.GetRequiredService<TransportAuthorizedPrincipalSource>().Assume(caller);

            await scope.ServiceProvider
                .GetRequiredService<StreamedDiscoveryRun>()
                .RunAsync(question, journal, this.lifetime.ApplicationStopping);
        }
        catch (Exception failure)
        {
            this.LogRunFailedWithoutANameForIt(failure);

            journal.Append(new DiscoveryRunFailed(DiscoveryRunFailure.Failed));
        }
        finally
        {
            this.registry.MarkEnded(journal.Id);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "A Discover run ended on a failure it had no name for, so its client was told only that it failed. "
            + "Neither the question nor the mail it read is in this record; what failed is this deployment's own "
            + "composition or a dependency it called.")]
    private partial void LogRunFailedWithoutANameForIt(Exception failure);
}
