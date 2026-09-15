// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MailFathom.AppHost;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>The administrative acts the composed host's own surface is driven through.</summary>
/// <remarks>
/// <para>
/// A deployment reads no user and no credential from its own file: both are rows, recorded through the administrative
/// endpoint. So everything a composed-host test needs to exist before it can authenticate is written here, through the
/// same routes <c>mfctl user add</c> and <c>mfctl credential create</c> reach, rather than seeded into the database
/// behind the running host's back.
/// </para>
/// <para>
/// The administrative key is the app model's, and it is deliberately not a key any user holds — administering the
/// service and reading a mailbox are different authorities, which is what lets the suite observe that neither one's
/// credential authenticates the other.
/// </para>
/// </remarks>
internal static class ComposedHostAdministration
{
    /// <summary>Opens a client aimed at the administrative surface, already carrying the app model's key.</summary>
    /// <param name="adminAddress">The base address of the composed host's administrative endpoint.</param>
    /// <returns>The client, which the caller disposes.</returns>
    internal static HttpClient Open(Uri adminAddress)
    {
        var client = new HttpClient { BaseAddress = adminAddress };
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", OrchestrationContract.AdminApiKey);

        return client;
    }

    /// <summary>Records one user under the label they are told apart by, and reports the identifier minted for them.</summary>
    /// <param name="client">A client aimed at the administrative surface.</param>
    /// <param name="displayName">The label, which is unique across the deployment.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The identifier the deployment generated.</returns>
    internal static async Task<Guid> RecordUserAsync(
        HttpClient client,
        string displayName,
        CancellationToken cancellationToken)
    {
        using var content = new StringContent(
            new JsonObject { ["displayName"] = displayName }.ToJsonString(),
            Encoding.UTF8,
            "application/json");
        using var response = await client.PostAsync(new Uri("api/admin/users", UriKind.Relative), content, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var provisioned = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        return provisioned.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Provisions an API key one user's MCP client presents, and reports the key the deployment minted.</summary>
    /// <param name="client">A client aimed at the administrative surface.</param>
    /// <param name="user">The user the key authenticates.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The key, which the deployment reports once and never again.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the answer carried no key, which would leave every later call unauthenticated for a reason nothing else would name.</exception>
    /// <remarks>
    /// The grant is left unstated, which is the entry that reaches everything the surface publishes: what this suite
    /// asserts about the MCP endpoint is the controls in front of it rather than a narrowed credential, and the one
    /// test that is about a grant states its own.
    /// </remarks>
    internal static async Task<string> ProvisionApiKeyAsync(
        HttpClient client,
        Guid user,
        CancellationToken cancellationToken)
    {
        using var content = new StringContent(
            new JsonObject { ["method"] = "api-key", ["permissions"] = null }.ToJsonString(),
            Encoding.UTF8,
            "application/json");
        using var response = await client.PostAsync(
            new Uri($"api/admin/users/{user:D}/credentials", UriKind.Relative),
            content,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        using var provisioned = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        return provisioned.RootElement.TryGetProperty("key", out var key) && key.GetString() is { } minted
            ? minted
            : throw new InvalidOperationException(
                "The composed host provisioned an API key credential and reported no key, so nothing can present it.");
    }

    /// <summary>Erases one user, and everything hung on them with it.</summary>
    /// <param name="client">A client aimed at the administrative surface.</param>
    /// <param name="user">The user to erase.</param>
    /// <remarks>
    /// Uncancellable by construction, for the reason <c>OrchestratedForeignUser</c> gives: it runs in a teardown, and a
    /// token already cancelled by the failure that sent the test there would leave the deployment holding a second user
    /// — which breaks every later start rather than the test that wrote it.
    /// </remarks>
    internal static async Task EraseUserAsync(HttpClient client, Guid user)
    {
        using var response = await client.DeleteAsync(
            new Uri($"api/admin/users/{user:D}", UriKind.Relative),
            CancellationToken.None);

        response.EnsureSuccessStatusCode();
    }
}
