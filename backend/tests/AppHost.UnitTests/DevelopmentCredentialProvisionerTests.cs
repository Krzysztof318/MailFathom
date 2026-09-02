// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.AppHost.UnitTests;

public sealed class DevelopmentCredentialProvisionerTests
{
    private static readonly Uri StartedEndpoint = new("http://127.0.0.1:5100/started");
    private static readonly Uri AdminEndpoint = new("http://127.0.0.1:5200/");
    private static readonly Guid OwnerId = Guid.Parse("b107de3d-4331-4755-8b17-3270dbe53b59");

    [Fact]
    public async Task EnsureAsync_CredentialDoesNotExist_ProvisionsItAfterTheHostStarts()
    {
        // Arrange
        using var responses = new RecordingHandler(
            Response(HttpStatusCode.ServiceUnavailable),
            Response(HttpStatusCode.OK),
            JsonResponse($$"""{"owners":[{"id":"{{OwnerId}}","served":true}]}"""),
            JsonResponse($$"""{"owner":"{{OwnerId}}","credentials":[]}"""),
            JsonResponse("{}"));
        using var client = new HttpClient(responses);
        var timeProvider = new FakeTimeProvider();
        var provisioner = new DevelopmentCredentialProvisioner(client, timeProvider);

        // Act
        var provisioning = provisioner.EnsureAsync(
            StartedEndpoint,
            AdminEndpoint,
            "test",
            "test-password",
            TestContext.Current.CancellationToken);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var credentialCreated = await provisioning;

        // Assert
        Assert.True(credentialCreated);
        Assert.Equal(
            [
                $"GET {StartedEndpoint}",
                $"GET {StartedEndpoint}",
                "GET http://127.0.0.1:5200/api/admin/owners",
                $"GET http://127.0.0.1:5200/api/admin/owners/{OwnerId:D}/credentials",
                $"POST http://127.0.0.1:5200/api/admin/owners/{OwnerId:D}/credentials",
            ],
            responses.Requests.Select(static request => $"{request.Method} {request.Address}"));
        Assert.Equal(
            "{\"method\":\"password\",\"username\":\"test\",\"password\":\"test-password\",\"permissions\":null}",
            responses.Requests[^1].Body);
    }

    [Fact]
    public async Task EnsureAsync_CredentialAlreadyExists_LeavesItUnchanged()
    {
        // Arrange
        using var responses = new RecordingHandler(
            Response(HttpStatusCode.OK),
            JsonResponse($$"""{"owners":[{"id":"{{OwnerId}}","served":true}]}"""),
            JsonResponse(
                $$"""{"owner":"{{OwnerId}}","credentials":[{"method":"password","lookup":"test","enabled":true}]}"""));
        using var client = new HttpClient(responses);
        var provisioner = new DevelopmentCredentialProvisioner(client, TimeProvider.System);

        // Act
        var credentialCreated = await provisioner.EnsureAsync(
            StartedEndpoint,
            AdminEndpoint,
            "test",
            "test-password",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(credentialCreated);
        Assert.Equal(3, responses.Requests.Count);
    }

    private static HttpResponseMessage Response(HttpStatusCode statusCode) => new(statusCode);

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
    };

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
