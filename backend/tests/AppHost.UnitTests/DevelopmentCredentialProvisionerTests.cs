// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.AppHost.UnitTests;

public sealed class DevelopmentCredentialProvisionerTests
{
    private static readonly Uri StartedEndpoint = new("http://127.0.0.1:5100/started");
    private static readonly Uri AdminEndpoint = new("http://127.0.0.1:5200/");
    private static readonly Guid UserId = Guid.Parse("b107de3d-4331-4755-8b17-3270dbe53b59");

    [Fact]
    public async Task WaitForSoleServedUserAsync_AHostStillStarting_AsksAgainAndThenNamesTheOneUserItHolds()
    {
        // Arrange
        using var responses = new RecordingHandler(
            Response(HttpStatusCode.ServiceUnavailable),
            Response(HttpStatusCode.OK),
            JsonResponse($$"""{"users":[{"id":"{{UserId}}","served":true}]}"""));
        using var client = new HttpClient(responses);
        var timeProvider = new FakeTimeProvider();
        var provisioner = new DevelopmentCredentialProvisioner(client, timeProvider);

        // Act
        var reading = provisioner.WaitForSoleServedUserAsync(
            StartedEndpoint,
            AdminEndpoint,
            TestContext.Current.CancellationToken);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var user = await reading;

        // Assert
        Assert.Equal(UserId, user);
        Assert.Equal(
            [
                $"GET {StartedEndpoint}",
                $"GET {StartedEndpoint}",
                "GET http://127.0.0.1:5200/api/admin/users",
            ],
            responses.Requests.Select(static request => $"{request.Method} {request.Address}"));
    }

    [Fact]
    public async Task WaitForSoleServedUserAsync_AHostHoldingNobody_RecordsTheUserAndNamesThem()
    {
        // Arrange
        using var responses = new RecordingHandler(
            Response(HttpStatusCode.OK),
            JsonResponse("""{"users":[]}"""),
            JsonResponse($$"""{"id":"{{UserId}}"}"""));
        using var client = new HttpClient(responses);
        var provisioner = new DevelopmentCredentialProvisioner(client, new FakeTimeProvider());

        // Act
        var user = await provisioner.WaitForSoleServedUserAsync(
            StartedEndpoint,
            AdminEndpoint,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(UserId, user);
        Assert.Equal(
            [
                $"GET {StartedEndpoint}",
                "GET http://127.0.0.1:5200/api/admin/users",
                "POST http://127.0.0.1:5200/api/admin/users",
            ],
            responses.Requests.Select(static request => $"{request.Method} {request.Address}"));
        using var body = JsonDocument.Parse(responses.Requests[^1].Body!);
        Assert.Equal("user", body.RootElement.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task EnsureCredentialAsync_CredentialDoesNotExist_ProvisionsIt()
    {
        // Arrange
        using var responses = new RecordingHandler(
            JsonResponse($$"""{"user":"{{UserId}}","credentials":[]}"""),
            JsonResponse("{}"));
        using var client = new HttpClient(responses);
        var provisioner = new DevelopmentCredentialProvisioner(client, TimeProvider.System);

        // Act
        var credentialCreated = await provisioner.EnsureCredentialAsync(
            AdminEndpoint,
            UserId,
            "test",
            "test-password",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(credentialCreated);
        Assert.Equal(
            [
                $"GET http://127.0.0.1:5200/api/admin/users/{UserId:D}/credentials",
                $"POST http://127.0.0.1:5200/api/admin/users/{UserId:D}/credentials",
            ],
            responses.Requests.Select(static request => $"{request.Method} {request.Address}"));
        Assert.Equal(
            "{\"method\":\"password\",\"username\":\"test\",\"password\":\"test-password\",\"permissions\":null}",
            responses.Requests[^1].Body);
    }

    [Fact]
    public async Task EnsureCredentialAsync_CredentialAlreadyExists_LeavesItUnchanged()
    {
        // Arrange
        using var responses = new RecordingHandler(
            JsonResponse(
                $$"""{"user":"{{UserId}}","credentials":[{"method":"password","lookup":"test","enabled":true}]}"""));
        using var client = new HttpClient(responses);
        var provisioner = new DevelopmentCredentialProvisioner(client, TimeProvider.System);

        // Act
        var credentialCreated = await provisioner.EnsureCredentialAsync(
            AdminEndpoint,
            UserId,
            "test",
            "test-password",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(credentialCreated);
        Assert.Single(responses.Requests);
    }

    [Fact]
    public async Task EnsureMailAccountAsync_ARecordDeclaringNoMailbox_DeclaresTheOneTheRunCollected()
    {
        // Arrange
        using var responses = new RecordingHandler(
            RecordResponse(version: 4, document: "{}"),
            JsonResponse("""{"committed":true,"version":5,"code":null,"messages":[]}"""));
        using var client = new HttpClient(responses);
        var provisioner = new DevelopmentCredentialProvisioner(client, TimeProvider.System);

        // Act
        var declared = await provisioner.EnsureMailAccountAsync(
            AdminEndpoint,
            UserId,
            "imap.example.test",
            "someone@example.test",
            "mailbox-password",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(declared);
        Assert.Equal(
            [
                $"GET http://127.0.0.1:5200/api/admin/users/{UserId:D}/record",
                $"POST http://127.0.0.1:5200/api/admin/users/{UserId:D}/record/mail-accounts",
            ],
            responses.Requests.Select(static request => $"{request.Method} {request.Address}"));

        using var body = JsonDocument.Parse(responses.Requests[^1].Body!);
        Assert.Equal(4, body.RootElement.GetProperty("version").GetInt64());

        using var account = JsonDocument.Parse(body.RootElement.GetProperty("account").GetString()!);
        Assert.Equal(
            OrchestrationContract.DevelopmentMailAccountId,
            account.RootElement.GetProperty("AccountId").GetString());
        Assert.Equal(
            OrchestrationContract.DevelopmentMailAccountDisplayName,
            account.RootElement.GetProperty("DisplayName").GetString());
        Assert.Equal("imap.example.test", account.RootElement.GetProperty("Host").GetString());
        Assert.Equal("someone@example.test", account.RootElement.GetProperty("UserName").GetString());

        var password = account.RootElement.GetProperty("Secrets").GetProperty("Password");
        Assert.Equal(
            OrchestrationContract.DevelopmentMailAccountPasswordName,
            password.GetProperty("Name").GetString());
        Assert.Equal("plaintext:mailbox-password", password.GetProperty("SecretReference").GetString());
    }

    [Fact]
    public async Task EnsureMailAccountAsync_ARecordAlreadyDeclaringThatMailbox_LeavesItUnchanged()
    {
        // Arrange
        using var responses = new RecordingHandler(
            RecordResponse(
                version: 7,
                document: $$"""{"MailAccounts":[{"AccountId":"{{OrchestrationContract.DevelopmentMailAccountId}}"}]}"""));
        using var client = new HttpClient(responses);
        var provisioner = new DevelopmentCredentialProvisioner(client, TimeProvider.System);

        // Act
        var declared = await provisioner.EnsureMailAccountAsync(
            AdminEndpoint,
            UserId,
            "imap.example.test",
            "someone@example.test",
            "mailbox-password",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(declared);
        Assert.Single(responses.Requests);
    }

    [Fact]
    public async Task EnsureMailAccountAsync_ADeploymentRefusingTheDeclaration_ReportsWhatItRefused()
    {
        // Arrange
        using var responses = new RecordingHandler(
            RecordResponse(version: 4, document: "{}"),
            JsonResponse(
                """{"committed":false,"version":4,"code":12040,"messages":["The mailbox names no host."]}"""));
        using var client = new HttpClient(responses);
        var provisioner = new DevelopmentCredentialProvisioner(client, TimeProvider.System);

        // Act
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => provisioner.EnsureMailAccountAsync(
            AdminEndpoint,
            UserId,
            "imap.example.test",
            "someone@example.test",
            "mailbox-password",
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("The mailbox names no host.", refused.Message, StringComparison.Ordinal);
    }

    private static HttpResponseMessage Response(HttpStatusCode statusCode) => new(statusCode);

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
    };

    /// <summary>Answers a record reading, whose document travels as a JSON string rather than as an object.</summary>
    private static HttpResponseMessage RecordResponse(long version, string document) => JsonResponse(
        $$"""
        {"user":"{{UserId}}","displayName":"Local","version":{{version}},"document":{{JsonSerializer.Serialize(document)}}}
        """);

    private sealed class RecordingHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);

        internal List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            this.Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken)));

            return this.responses.Dequeue();
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Address, string? Body);
}
