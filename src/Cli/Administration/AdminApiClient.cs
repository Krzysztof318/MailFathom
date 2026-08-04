// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MailFathom.Cli.Administration;

/// <summary>Reaches the administrative endpoint of one deployment.</summary>
/// <remarks>
/// Every operation the command performs is a request through here. There is no second path to a deployment — no
/// configuration file it reads, no database it opens — so what this class can do is the whole of what the command can
/// do, and the endpoint's own authentication is what bounds it.
/// </remarks>
internal sealed class AdminApiClient
{
    private readonly HttpClient transport;

    /// <summary>Initializes a client over a transport the caller owns.</summary>
    /// <param name="transport">The transport, whose <see cref="HttpClient.BaseAddress" /> names the deployment.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="transport" /> is <see langword="null" />.</exception>
    internal AdminApiClient(HttpClient transport)
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

    /// <summary>Turns a transport failure into something the operator can act on.</summary>
    /// <remarks>A cancelled request is left alone: the operator interrupted it, and reporting that as a deployment problem would be wrong.</remarks>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await this.transport.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CliFailure($"The deployment at {this.transport.BaseAddress} did not answer in time.");
        }
        catch (HttpRequestException failure)
        {
            throw new CliFailure($"The deployment at {this.transport.BaseAddress} could not be reached: {failure.Message}", failure);
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
