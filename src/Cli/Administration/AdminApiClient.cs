// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MailFathom.Cli.Administration.Embeddings;
using MailFathom.Cli.Administration.Folders;
using MailFathom.Cli.Administration.Jobs;
using MailFathom.Cli.Administration.Mailboxes;
using MailFathom.Cli.Administration.Rules;
using MailFathom.Cli.Administration.Spam;
using MailFathom.Cli.Transport;
using MailFathom.Versioning;

namespace MailFathom.Cli.Administration;

/// <summary>Reaches the administrative endpoint of one deployment.</summary>
/// <remarks>
/// <para>
/// Every operation the command performs is a request through here. There is no second path to a deployment — no
/// configuration file it reads, no database it opens — so what this class can do is the whole of what the command can
/// do, and the endpoint's own authentication is what bounds it.
/// </para>
/// <para>
/// Being that one path is also what makes it the place the two versions are settled. The session is read before the
/// first operation whichever command is running, so a deployment this build cannot be sure it speaks to is refused
/// before anything is asked of it rather than after — an activation that would start a provider bill is the case that
/// decides where the check belongs. It happens once per client, and a client is made once per command, so an operator
/// running a command whose builds merely differ is told once rather than once per request.
/// </para>
/// </remarks>
internal sealed class AdminApiClient
{
    private static readonly string CommandVersion =
        StampedAssemblyVersion.ReadFrom(typeof(AdminApiClient).Assembly).Version;

    private readonly DeploymentTransport transport;
    private readonly ICliConsole console;

    private AdminSession? settledSession;

    /// <summary>Initializes a client over a transport the caller owns.</summary>
    /// <param name="transport">The transport, whose base address names the deployment.</param>
    /// <param name="console">The terminal a version difference the command carries on past is reported to.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal AdminApiClient(DeploymentTransport transport, ICliConsole console)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(console);

        this.transport = transport;
        this.console = console;
    }

    /// <summary>Asks the deployment who the presented credential makes the caller.</summary>
    /// <param name="token">The bearer credential to present.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>What the deployment reports about the caller.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="token" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the deployment refused the credential, could not be reached, or answered with something that is not a session.</exception>
    /// <remarks>
    /// This is what makes <c>login</c> more than writing a file: a credential is stored only after the deployment has
    /// confirmed it accepts it, so a mistyped key fails at the terminal rather than at the next command.
    /// </remarks>
    internal async Task<AdminSession> ReadSessionAsync(string token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        using var request = new HttpRequestMessage(HttpMethod.Get, AdminEndpointRoutes.SessionPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await this.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new CliFailure(
                "The deployment refused the credential. Check that it is one the administrative endpoint is configured with, and that it has not expired.");
        }

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            throw new CliFailure(
                $"The address answered, but serves no administrative endpoint at {AdminEndpointRoutes.SessionPath}. Check the port: the administrative endpoint binds a listener of its own, and it is disabled unless the deployment enabled it.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new CliFailure($"The deployment answered {(int)response.StatusCode} rather than a session.");
        }

        var session = await ReadSessionBodyAsync(response, cancellationToken);

        this.Settle(session);

        return session;
    }

    /// <summary>Hands a deployment the refresh token it should keep for one of its mail accounts.</summary>
    /// <param name="token">The bearer credential to present.</param>
    /// <param name="account">The account the grant acts for, as the deployment's configuration names it.</param>
    /// <param name="refreshToken">The token the authorization run produced.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once the deployment has stored the token.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the deployment refused the credential or the grant, could not be reached, or answered with anything but an acceptance.</exception>
    /// <remarks>
    /// The one request this command sends that carries a credential of somebody else's. It is presented once and never
    /// echoed: the deployment answers with no body, so a success is the status alone and there is nothing here that
    /// could report the token back.
    /// </remarks>
    internal async Task StoreMailboxRefreshTokenAsync(
        string token,
        string account,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(refreshToken);

        await this.EnsureSettledAsync(token, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, AdminEndpointRoutes.MailboxRefreshTokenPath)
        {
            Content = JsonContent.Create(
                new MailboxRefreshTokenRequest(account, refreshToken),
                CliJsonContext.Default.MailboxRefreshTokenRequest),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await this.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new CliFailure(
                "The deployment refused the credential. Check that it is one the administrative endpoint is configured with, and that it has not expired.");
        }

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            throw new CliFailure(
                $"The address answered, but serves no administrative endpoint at {AdminEndpointRoutes.MailboxRefreshTokenPath}. Check the port: the administrative endpoint binds a listener of its own, and it is disabled unless the deployment enabled it.");
        }

        if (response.StatusCode is HttpStatusCode.BadRequest)
        {
            throw new CliFailure(
                await ReadRefusalAsync(response, cancellationToken)
                ?? "The deployment refused the grant without saying why.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new CliFailure($"The deployment answered {(int)response.StatusCode} rather than storing the token.");
        }
    }

    /// <summary>Asks the deployment what its mail synchronization is doing.</summary>
    /// <param name="token">The bearer credential to present.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>What the deployment reports about each configured account and each of its folders.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="token" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the deployment refused the credential, could not be reached, or answered with something that is not a status.</exception>
    internal Task<MailboxSynchronizationStatus> ReadMailboxSynchronizationStatusAsync(
        string token,
        CancellationToken cancellationToken) =>
        this.RequestAsync(
            HttpMethod.Get,
            AdminEndpointRoutes.MailboxSynchronizationPath,
            token,
            CliJsonContext.Default.MailboxSynchronizationStatus,
            cancellationToken);

    /// <summary>Asks the deployment where its semantic search stands.</summary>
    /// <param name="token">The bearer credential to present.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>What the deployment reports about its embedding profile, its provider, and its budget.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="token" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the deployment refused the credential, could not be reached, or answered with something that is not a status.</exception>
    internal Task<EmbeddingStatus> ReadEmbeddingStatusAsync(string token, CancellationToken cancellationToken) =>
        this.RequestAsync(
            HttpMethod.Get,
            AdminEndpointRoutes.EmbeddingStatusPath,
            token,
            CliJsonContext.Default.EmbeddingStatus,
            cancellationToken);

    /// <summary>Asks the deployment what activating its declaration would do, without activating anything.</summary>
    /// <param name="token">The bearer credential to present.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The assessment the operator is asked to confirm against.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="token" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the deployment declares no provider, refused the credential, could not be reached, or answered with something that is not an assessment.</exception>
    internal Task<EmbeddingActivationAssessment> ReadEmbeddingActivationAsync(
        string token,
        CancellationToken cancellationToken) =>
        this.RequestAsync(
            HttpMethod.Get,
            AdminEndpointRoutes.EmbeddingActivationPath,
            token,
            CliJsonContext.Default.EmbeddingActivationAssessment,
            cancellationToken);

    /// <summary>Tells the deployment to take up its declaration and begin embedding under it.</summary>
    /// <param name="token">The bearer credential to present.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>What the activation did, and the estimate it was weighed as.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="token" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the deployment refused the activation, refused the credential, could not be reached, or answered with something that is not an activation.</exception>
    /// <remarks>
    /// The one request this command sends that starts a provider bill. It carries no body: what is activated is what the
    /// deployment's own configuration declares, so there is nothing here a caller could get wrong or a proxy could
    /// alter.
    /// </remarks>
    internal Task<EmbeddingActivation> ActivateEmbeddingProfileAsync(
        string token,
        CancellationToken cancellationToken) =>
        this.RequestAsync(
            HttpMethod.Post,
            AdminEndpointRoutes.EmbeddingActivationPath,
            token,
            CliJsonContext.Default.EmbeddingActivation,
            cancellationToken);

    /// <summary>Tells the deployment to stop the reindex it has under way.</summary>
    /// <param name="token">The bearer credential to present.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Whether a reindex was abandoned, or none was running.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="token" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the deployment refused the credential, could not be reached, or answered with something that is not a cancellation.</exception>
    internal Task<EmbeddingReindexCancellation> CancelEmbeddingReindexAsync(
        string token,
        CancellationToken cancellationToken) =>
        this.RequestAsync(
            HttpMethod.Post,
            AdminEndpointRoutes.EmbeddingReindexCancellationPath,
            token,
            CliJsonContext.Default.EmbeddingReindexCancellation,
            cancellationToken);

    /// <summary>Asks the deployment which mail rules it has loaded.</summary>
    /// <param name="token">The bearer credential to present.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The rule set in force, and whether the configuration on disk is the one it was read from.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="token" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the deployment refused the credential, could not be reached, or answered with something that is not a rule set.</exception>
    internal Task<LoadedRuleSet> ReadRulesAsync(string token, CancellationToken cancellationToken) =>
        this.RequestAsync(
            HttpMethod.Get,
            AdminEndpointRoutes.RulesPath,
            token,
            CliJsonContext.Default.LoadedRuleSet,
            cancellationToken);

    /// <summary>Asks the deployment to run one account's rules over every message it holds for that account.</summary>
    /// <param name="token">The bearer credential to present.</param>
    /// <param name="account">The account the run is asked for, as the deployment's configuration names it.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The run the account now has, and whether this request is what started it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the deployment refused the request or the credential, could not be reached, or answered with something that is not a run.</exception>
    /// <remarks>
    /// It returns as soon as the deployment has written the request down. The run itself is carried by the account's
    /// synchronization runs, so this command is never what keeps a walk of a mailbox alive and closing the terminal
    /// cannot cancel one.
    /// </remarks>
    internal Task<MailRuleRunStart> StartRuleRunAsync(
        string token,
        string account,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        return this.RequestAsync(
            HttpMethod.Post,
            AdminEndpointRoutes.RuleRunsPath,
            token,
            CliJsonContext.Default.MailRuleRunStart,
            cancellationToken,
            JsonContent.Create(new MailRuleRunRequest(account), CliJsonContext.Default.MailRuleRunRequest));
    }

    /// <summary>Asks the deployment where one account's whole-mailbox run has got to.</summary>
    /// <param name="token">The bearer credential to present.</param>
    /// <param name="account">The account whose run is read.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The run, which is absent where the account has never been asked for one.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the deployment refused the request or the credential, could not be reached, or answered with something that is not a run.</exception>
    internal Task<MailRuleRunState> ReadRuleRunAsync(
        string token,
        string account,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        return this.RequestAsync(
            HttpMethod.Get,
            $"{AdminEndpointRoutes.RuleRunsPath}?account={Uri.EscapeDataString(account)}",
            token,
            CliJsonContext.Default.MailRuleRunState,
            cancellationToken);
    }

    /// <summary>Asks the deployment what its rules concluded about one account's mail.</summary>
    /// <param name="token">The bearer credential to present.</param>
    /// <param name="query">Which account, and how the page is narrowed and continued.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>One page of the history, and the cursor the next page is asked with where one exists.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the deployment refused the request or the credential, could not be reached, or answered with something that is not a page.</exception>
    internal Task<MailRuleHistoryPage> ReadRuleHistoryAsync(
        string token,
        MailRuleHistoryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return this.RequestAsync(
            HttpMethod.Get,
            $"{AdminEndpointRoutes.RuleHistoryPath}{query.ToQueryString()}",
            token,
            CliJsonContext.Default.MailRuleHistoryPage,
            cancellationToken);
    }

    /// <summary>Asks the deployment to classify every message it holds for one account.</summary>
    /// <param name="token">The bearer credential to present.</param>
    /// <param name="request">The account, the folders to walk, and the two switches.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The run the account now has, and whether this request is what started it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the deployment refused the request or the credential, could not be reached, or answered with something that is not a run.</exception>
    /// <remarks>
    /// It returns as soon as the deployment has written the request down. The run itself is carried by the account's
    /// synchronization runs, so this command is never what keeps a walk of a mailbox alive and closing the terminal
    /// cannot cancel one — including a run that was asked to act on the mailbox.
    /// </remarks>
    internal Task<SpamClassificationRunStart> StartSpamClassificationRunAsync(
        string token,
        SpamClassificationRunRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return this.RequestAsync(
            HttpMethod.Post,
            AdminEndpointRoutes.SpamClassificationRunsPath,
            token,
            CliJsonContext.Default.SpamClassificationRunStart,
            cancellationToken,
            JsonContent.Create(request, CliJsonContext.Default.SpamClassificationRunRequest));
    }

    /// <summary>Asks the deployment where one account's whole-mailbox classification run has got to.</summary>
    /// <param name="token">The bearer credential to present.</param>
    /// <param name="account">The account whose run is read.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The run, which is absent where the account has never been asked for one.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the deployment refused the request or the credential, could not be reached, or answered with something that is not a run.</exception>
    internal Task<SpamClassificationRunState> ReadSpamClassificationRunAsync(
        string token,
        string account,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        return this.RequestAsync(
            HttpMethod.Get,
            $"{AdminEndpointRoutes.SpamClassificationRunsPath}?account={Uri.EscapeDataString(account)}",
            token,
            CliJsonContext.Default.SpamClassificationRunState,
            cancellationToken);
    }

    /// <summary>Asks the deployment what classification concluded about one account's mail.</summary>
    /// <param name="token">The bearer credential to present.</param>
    /// <param name="query">Which account, and how the page is narrowed and continued.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>One page of the classifications, and the cursor the next page is asked with where one exists.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the deployment refused the request or the credential, could not be reached, or answered with something that is not a page.</exception>
    internal Task<SpamClassificationPage> ReadSpamClassificationsAsync(
        string token,
        SpamClassificationQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return this.RequestAsync(
            HttpMethod.Get,
            $"{AdminEndpointRoutes.SpamClassificationsPath}{query.ToQueryString()}",
            token,
            CliJsonContext.Default.SpamClassificationPage,
            cancellationToken);
    }

    /// <summary>Asks the deployment which background work it will not attempt again.</summary>
    /// <param name="token">The bearer credential to present.</param>
    /// <param name="query">How the page is narrowed and continued.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>One page of the dead letters, and the cursor the next page is asked with where one exists.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the deployment refused the request or the credential, could not be reached, or answered with something that is not a page.</exception>
    internal Task<DeadLetteredJobPage> ReadDeadLetteredJobsAsync(
        string token,
        DeadLetteredJobQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return this.RequestAsync(
            HttpMethod.Get,
            $"{AdminEndpointRoutes.JobDeadLettersPath}{query.ToQueryString()}",
            token,
            CliJsonContext.Default.DeadLetteredJobPage,
            cancellationToken);
    }

    /// <summary>Asks the deployment to run one dead letter again, under the identity it already carries.</summary>
    /// <param name="token">The bearer credential to present.</param>
    /// <param name="job">The job to attempt again.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>What became of the job.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="token" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the deployment refused the request or the credential, could not be reached, or answered with something that is not an outcome.</exception>
    /// <remarks>
    /// It returns as soon as the deployment has written the decision down. The work itself is run by whichever worker
    /// claims the job next, so this command is never what carries it out and closing the terminal cannot stop it.
    /// </remarks>
    internal Task<JobRecovery> RetryDeadLetteredJobAsync(
        string token,
        Guid job,
        CancellationToken cancellationToken) =>
        this.RequestAsync(
            HttpMethod.Post,
            AdminEndpointRoutes.JobRetryPath,
            token,
            CliJsonContext.Default.JobRecovery,
            cancellationToken,
            JsonContent.Create(new JobRecoveryRequest(job), CliJsonContext.Default.JobRecoveryRequest));

    /// <summary>Asks the deployment to record that one dead letter will never be run.</summary>
    /// <param name="token">The bearer credential to present.</param>
    /// <param name="job">The job to drop.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>What became of the job.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="token" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the deployment refused the request or the credential, could not be reached, or answered with something that is not an outcome.</exception>
    /// <remarks>The record is kept rather than removed, so the job goes on being readable as one somebody decided about.</remarks>
    internal Task<JobRecovery> DropDeadLetteredJobAsync(
        string token,
        Guid job,
        CancellationToken cancellationToken) =>
        this.RequestAsync(
            HttpMethod.Post,
            AdminEndpointRoutes.JobDropPath,
            token,
            CliJsonContext.Default.JobRecovery,
            cancellationToken,
            JsonContent.Create(new JobRecoveryRequest(job), CliJsonContext.Default.JobRecoveryRequest));

    /// <summary>Asks the deployment to erase one bounded pass of a folder's stored mail.</summary>
    /// <param name="token">The bearer credential to present.</param>
    /// <param name="account">The account the folder belongs to, as the deployment's configuration names it.</param>
    /// <param name="folder">MailFathom's own alias for the folder.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>What the pass erased, and whether the folder still holds stored mail.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the deployment refused the request or the credential, could not be reached, or answered with something that is not an erasure.</exception>
    /// <remarks>
    /// One pass per request, and the command sends as many as the folder needs. The work is a local transaction rather
    /// than anything on a mail server, so the answer arrives when the pass has committed and what it reports is what
    /// the deployment has already disposed of.
    /// </remarks>
    internal Task<MailFolderErasure> EraseFolderMirrorAsync(
        string token,
        string account,
        string folder,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(folder);

        return this.RequestAsync(
            HttpMethod.Post,
            AdminEndpointRoutes.FolderErasurePath,
            token,
            CliJsonContext.Default.MailFolderErasure,
            cancellationToken,
            JsonContent.Create(
                new MailFolderErasureRequest(account, folder),
                CliJsonContext.Default.MailFolderErasureRequest));
    }

    /// <summary>Sends one credentialed request and reads the answer, or turns the refusal into a sentence.</summary>
    /// <remarks>
    /// <para>
    /// Written once for every route that answers with a document, because the four refusals are the same four
    /// everywhere: a credential this deployment does not accept, a port serving no administrative endpoint, a request
    /// this deployment states a reason for refusing, and anything else. Repeating them per operation is how they drift
    /// into saying different things about the same situation.
    /// </para>
    /// <para>
    /// A stated refusal is repeated rather than replaced. The deployment knows why it refused — no provider declared, a
    /// reindex already running, an estimate above the ceiling — and inventing a sentence here would lose the two numbers
    /// the operator needs.
    /// </para>
    /// </remarks>
    private async Task<TAnswer> RequestAsync<TAnswer>(
        HttpMethod method,
        string path,
        string token,
        JsonTypeInfo<TAnswer> answerContract,
        CancellationToken cancellationToken,
        HttpContent? content = null)
        where TAnswer : class
    {
        ArgumentNullException.ThrowIfNull(token);

        await this.EnsureSettledAsync(token, cancellationToken);

        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await this.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new CliFailure(
                "The deployment refused the credential. Check that it is one the administrative endpoint is configured with, and that it has not expired.");
        }

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            throw new CliFailure(
                $"The address answered, but serves no administrative endpoint at {path}. Check the port: the administrative endpoint binds a listener of its own, and it is disabled unless the deployment enabled it.");
        }

        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
        {
            throw new CliFailure(
                await ReadRefusalAsync(response, cancellationToken)
                ?? "The deployment refused the request without saying why.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new CliFailure($"The deployment answered {(int)response.StatusCode} and said nothing this command could act on.");
        }

        try
        {
            return await response.Content.ReadFromJsonAsync(answerContract, cancellationToken)
                ?? throw new CliFailure("The deployment answered successfully with an empty body, which no operation here sends.");
        }
        catch (Exception failure) when (failure is JsonException or NotSupportedException)
        {
            throw new CliFailure(
                "The address answered, but not with anything MailFathom would send. Check that it is the administrative endpoint rather than another service on the same host.",
                failure);
        }
    }

    /// <summary>Reads the session where the command has not asked for one, so every operation is preceded by the version check.</summary>
    /// <remarks>
    /// One request, before the first operation and never again on the same client. <c>login</c> and <c>status</c> ask
    /// for the session themselves and therefore reach this having already settled, which is what keeps the check at one
    /// request per command whichever command is running.
    /// </remarks>
    private async Task EnsureSettledAsync(string token, CancellationToken cancellationToken)
    {
        if (this.settledSession is null)
        {
            _ = await this.ReadSessionAsync(token, cancellationToken);
        }
    }

    /// <summary>Applies what the deployment's version means for this command, and reports it where it means anything.</summary>
    /// <exception cref="CliFailure">Thrown when the deployment is from another release line, which no command is sent to.</exception>
    private void Settle(AdminSession session)
    {
        if (this.settledSession is not null)
        {
            return;
        }

        this.settledSession = session;

        var agreement = DeploymentVersionAgreement.Settle(CommandVersion, session.Version);

        if (agreement is { PermitsCommands: false, Concern: { } refusal })
        {
            throw new CliFailure(refusal);
        }

        if (agreement.Concern is { } warning)
        {
            this.console.WriteError(warning);
        }
    }

    /// <summary>Turns a transport failure into something the operator can act on.</summary>
    /// <remarks>
    /// A cancelled request is left alone: the operator interrupted it, and reporting that as a deployment problem would
    /// be wrong. A refused certificate is the one transport failure that has a cause worth naming rather than a
    /// message to repeat, because the platform reports it as an ordinary connection failure and the operator would
    /// otherwise go looking at the address, the port, and the firewall.
    /// </remarks>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await this.transport.Client.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CliFailure($"The deployment at {this.transport.Client.BaseAddress} did not answer in time.");
        }
        catch (HttpRequestException failure)
        {
            throw new CliFailure(
                this.transport.DescribeRefusal()
                ?? $"The deployment at {this.transport.Client.BaseAddress} could not be reached: {failure.Message}",
                failure);
        }
    }

    /// <summary>Reads the sentence a refusal carries, when it carries one.</summary>
    /// <remarks>
    /// A deployment states what was wrong with the request — an account it does not configure, a field the body omitted
    /// — and repeating that is more use than any wording invented here. Anything that is not a problem document is read
    /// as no reason rather than as a failure of its own, because the request was already refused and how the refusal
    /// was phrased is not the operator's problem to solve.
    /// </remarks>
    private static async Task<string?> ReadRefusalAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync(
                CliJsonContext.Default.AdminProblem,
                cancellationToken);

            return problem?.Detail is { Length: > 0 } stated ? stated : null;
        }
        catch (Exception failure) when (failure is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Reads the body as a session, refusing anything that merely happens to be JSON.</summary>
    /// <remarks>
    /// An address that answers with a success status is not yet a MailFathom deployment — a proxy, a login page, or an
    /// unrelated service can do that. Requiring the body to name the service is what keeps <c>login</c> from reporting
    /// success against something that never saw the credential.
    /// </remarks>
    private static async Task<AdminSession> ReadSessionBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        AdminSession? session;

        try
        {
            session = await response.Content.ReadFromJsonAsync(
                CliJsonContext.Default.AdminSession,
                cancellationToken);
        }
        catch (Exception failure) when (failure is JsonException or NotSupportedException)
        {
            throw new CliFailure(
                "The address answered, but not with anything MailFathom would send. Check that it is the administrative endpoint rather than another service on the same host.",
                failure);
        }

        if (session is null || !string.Equals(session.Service, "MailFathom", StringComparison.Ordinal))
        {
            throw new CliFailure(
                "The address answered, but did not identify itself as MailFathom. Check that it is the administrative endpoint rather than another service on the same host.");
        }

        return session;
    }
}
