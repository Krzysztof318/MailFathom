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
/// Two things bound it, and both are the run's own rather than the connection's. It is stopped when the process is
/// stopping, because a run holds a database connection and there is nobody left to answer; and it is stopped once it
/// has taken the longest a run may take, which is what ends a run whose provider never answered. Neither is the
/// client's connection: a person who closed the page does not stop a run, because they may reattach to it.
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
    /// <param name="logger">Reports a run this deployment could not compose at all, which nothing else would.</param>
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
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    /// <remarks>
    /// Nothing is awaited and nothing is observed, because the run reports itself: every ending it can reach is
    /// published to its own stream, so a task result would carry what the client has already been told.
    /// </remarks>
    internal void Start(MailQuestion question, DiscoveryRunJournal journal, AuthorizedPrincipal caller)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(caller);

        _ = Task.Run(() => this.ExecuteAsync(question, journal, caller));
    }

    /// <summary>Executes one run on a scope of its own and leaves it ended however it went.</summary>
    /// <remarks>
    /// The run publishes every ending it can reach itself, so the handler here is for the one it cannot: a scope this
    /// process could not compose the use case out of. That is a deployment fault rather than a run's, and it is the one
    /// failure nothing else would report — nothing awaits this task, so an exception escaping here would be observed by
    /// nobody and would leave a client watching a run that never ends.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Nothing awaits this task, so an escaping exception would be observed by nobody and would leave the run unended; it is logged and the run is ended instead.")]
    private async Task ExecuteAsync(MailQuestion question, DiscoveryRunJournal journal, AuthorizedPrincipal caller)
    {
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(this.lifetime.ApplicationStopping);
        stopping.CancelAfter(DiscoveryRunBounds.MaximumDuration);

        try
        {
            await using var scope = this.scopeFactory.CreateAsyncScope();

            scope.ServiceProvider.GetRequiredService<TransportAuthorizedPrincipalSource>().Assume(caller);

            await scope.ServiceProvider
                .GetRequiredService<StreamedDiscoveryRun>()
                .RunAsync(question, journal, stopping.Token);
        }
        catch (Exception composition)
        {
            this.LogRunNotComposed(composition);

            journal.Append(new DiscoveryRunFailed(DiscoveryRunFailure.Failed));
        }
        finally
        {
            this.registry.MarkEnded(journal.Id);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "A Discover run could not be composed, so it ended without answering. Neither the question nor the "
            + "mail it would have read is in this record; what failed is this deployment's own composition.")]
    private partial void LogRunNotComposed(Exception failure);
}
