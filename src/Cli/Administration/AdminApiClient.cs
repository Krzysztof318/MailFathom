// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MailFathom.Cli.Transport;

namespace MailFathom.Cli.Administration;

/// <summary>Reaches the administrative endpoint of one deployment.</summary>
/// <remarks>
/// Every operation the command performs is a request through here. There is no second path to a deployment — no
/// configuration file it reads, no database it opens — so what this class can do is the whole of what the command can
/// do, and the endpoint's own authentication is what bounds it.
/// </remarks>
internal sealed class AdminApiClient
{
    private readonly DeploymentTransport transport;

    /// <summary>Initializes a client over a transport the caller owns.</summary>
    /// <param name="transport">The transport, whose base address names the deployment.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="transport" /> is <see langword="null" />.</exception>
    internal AdminApiClient(DeploymentTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);

        this.transport = transport;
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

        return await ReadSessionBodyAsync(response, cancellationToken);
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
