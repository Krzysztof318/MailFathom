// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MailFathom.AppHost;
using MailFathom.IntegrationTests.Orchestration;
using Xunit;

namespace MailFathom.IntegrationTests.Hosting;

/// <summary>Proves the outbox routes are served, are behind the administrative credential, and split reading from deciding.</summary>
/// <remarks>
/// <para>
/// What only a composed host can establish is that these five routes exist at all and inherit the group's requirement.
/// The unit suite maps the group and reads the permission each route publishes, which says nothing about a process that
/// dropped one of them or a filter that was written and never attached — and an outbox served to an unauthenticated
/// caller is a list of what this owner is sending, while a decision route served to one is a way to put a message back
/// on its way to somebody's mailbox.
/// </para>
/// <para>
/// The split between the two permissions is asserted from where a caller stands, with a credential that holds the
/// reading grant and not the deciding one. That is the claim the endpoints were designed around — a monitoring
/// credential watches an outbox it cannot act on — and it is unobservable from anywhere the route metadata is merely
/// read.
/// </para>
/// <para>
/// Nothing here carries <c>[RequiresIntegrationCoverage]</c>, for the reason the security suites beside it state: the
/// classes exercised are either unit-covered already or belong to <c>Host</c>, which is outside the coverage
/// denominator.
/// </para>
/// </remarks>
[Collection(ComposedHostCollectionDefinition.Name)]
public sealed class ComposedOutboxEndpointTests
{
    /// <summary>The route one page of the recorded sends is read from.</summary>
    private const string OutboxRoute = "/api/admin/outbox";

    /// <summary>The permission both decisions publish, which is what a reading credential is refused them under.</summary>
    private const string OperatePermission = "mailfathom.admin.operate";

    /// <summary>The permission the one reading that names people publishes, which the other two do not.</summary>
    private const string AuditReadPermission = "mailfathom.admin.audit.read";

    private readonly MailFathomOrchestrationFixture orchestration;

    /// <summary>Initializes the tests against the assembly's orchestration.</summary>
    /// <param name="orchestration">The orchestration fixture, which starts the host on first request.</param>
    public ComposedOutboxEndpointTests(MailFathomOrchestrationFixture orchestration) =>
        this.orchestration = orchestration;

    /// <summary>Every one of the five, refused before anything about this deployment's mail is composed into an answer.</summary>
    [Theory]
    [InlineData("GET", OutboxRoute)]
    [InlineData("GET", $"{OutboxRoute}/summary")]
    [InlineData("GET", $"{OutboxRoute}/0199c0ff-ee00-7000-8000-000000000000")]
    [InlineData("POST", $"{OutboxRoute}/cancellation")]
    [InlineData("POST", $"{OutboxRoute}/requeue")]
    public async Task OutboxRoutes_ARequestCarryingNoCredential_AreRefusedBeforeTheOutboxIsRead(
        string method,
        string route)
    {
        // Arrange
        using var client = await this.orchestration.OpenAdminEndpointClientAsync(TestContext.Current.CancellationToken);
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(route, UriKind.Relative));

        // Act
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        Assert.DoesNotContain(
            "stage",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The grant split, in one test because it is one claim: the credential this deployment admits for reading reaches
    /// the two readings that name nobody, and is refused the reading that names people and both decisions, each under
    /// the permission it publishes.
    /// </summary>
    [Fact]
    public async Task OutboxRoutes_ACredentialHoldingTheReadingGrantAlone_ReadsTheCountsAndIsRefusedEverythingNamingAPerson()
    {
        // Arrange
        using var client = await this.orchestration.OpenAdminEndpointClientAsync(TestContext.Current.CancellationToken);
        var send = Guid.CreateVersion7();

        // Act
        using var summary = await SendAsync(
            client,
            HttpMethod.Get,
            $"{OutboxRoute}/summary",
            OrchestrationContract.AdminNarrowedApiKey);
        using var listing = await SendAsync(
            client,
            HttpMethod.Get,
            OutboxRoute,
            OrchestrationContract.AdminNarrowedApiKey);
        using var singleRecord = await SendAsync(
            client,
            HttpMethod.Get,
            $"{OutboxRoute}/{send:D}",
            OrchestrationContract.AdminNarrowedApiKey);
        using var cancellation = await SendAsync(
            client,
            HttpMethod.Post,
            $"{OutboxRoute}/cancellation",
            OrchestrationContract.AdminNarrowedApiKey,
            JsonContent.Create(new { outgoingEmail = send }));
        using var requeue = await SendAsync(
            client,
            HttpMethod.Post,
            $"{OutboxRoute}/requeue",
            OrchestrationContract.AdminNarrowedApiKey,
            JsonContent.Create(new { outgoingEmail = send, refusalRestated = true }));

        // Assert
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        Assert.Equal(HttpStatusCode.OK, listing.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, singleRecord.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, cancellation.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, requeue.StatusCode);

        Assert.Equal(AuditReadPermission, await PermissionRefusedUnder(singleRecord));
        Assert.Equal(OperatePermission, await PermissionRefusedUnder(cancellation));
        Assert.Equal(OperatePermission, await PermissionRefusedUnder(requeue));

        // The listing names no person, which is the reason it exists as a separate reading from the single record.
        using var page = JsonDocument.Parse(
            await listing.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.All(
            page.RootElement.GetProperty("sends").EnumerateArray(),
            entry =>
            {
                Assert.False(entry.TryGetProperty("recipients", out _));
                Assert.False(entry.TryGetProperty("subject", out _));
            });
    }

    /// <summary>
    /// A decision about a send this deployment does not hold, taken by the credential that may decide. It answers what
    /// became of the record it named rather than refusing the request, which is the whole difference between a decision
    /// and the reading beside it — and that reading answers <c>404</c> for the same identifier.
    /// </summary>
    [Fact]
    public async Task OutboxRoutes_ADecisionNamingASendThisDeploymentDoesNotHold_ReportsTheRecordAsUnknown()
    {
        // Arrange
        using var client = await this.orchestration.OpenAdminEndpointClientAsync(TestContext.Current.CancellationToken);
        var send = Guid.CreateVersion7();

        // Act
        using var cancellation = await SendAsync(
            client,
            HttpMethod.Post,
            $"{OutboxRoute}/cancellation",
            OrchestrationContract.AdminApiKey,
            JsonContent.Create(new { outgoingEmail = send }));
        using var reading = await SendAsync(
            client,
            HttpMethod.Get,
            $"{OutboxRoute}/{send:D}",
            OrchestrationContract.AdminApiKey);

        // Assert
        Assert.Equal(HttpStatusCode.OK, cancellation.StatusCode);

        using var decision = JsonDocument.Parse(
            await cancellation.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(send, decision.RootElement.GetProperty("outgoingEmail").GetGuid());
        Assert.Equal("RecordUnknown", decision.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(HttpStatusCode.NotFound, reading.StatusCode);
    }

    /// <summary>Reads the permission a refusal names, failing the test where it named none.</summary>
    private static async Task<string?> PermissionRefusedUnder(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return problem.RootElement.GetProperty("permission").GetString();
    }

    /// <summary>Sends one credentialed request to the administrative endpoint.</summary>
    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string route,
        string apiKey,
        HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(route, UriKind.Relative)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
