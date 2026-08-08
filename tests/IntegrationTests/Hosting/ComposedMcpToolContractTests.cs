// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MailFathom.AppHost;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.IntegrationTests.Orchestration;
using MailFathom.IntegrationTests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Hosting;

/// <summary>Proves that a tool call answers over the real transport with the shape the tool's contract publishes.</summary>
/// <remarks>
/// <para>
/// The sibling class proves what the controls in front of the endpoint do; nothing yet proves what the endpoint answers
/// when they let a call through. That gap is not about the use case, which the persistence tests already exercise
/// against the same database: it is about everything between a JSON-RPC request and a serialized result — that the tool
/// is discoverable under the name it declares, that its arguments bind from the wire, that a scope resolved from the
/// host's own configuration reaches the mail this database holds, and that the answer serializes into the fields the
/// tool's description tells a client to read. Each of those is decided by the composition and by the protocol library,
/// so a substitute for either would assert the arrangement rather than the contract.
/// </para>
/// <para>
/// The mail is seeded through the same composed services every persistence test writes with, because both open the one
/// orchestrated database. The host is a second reader of it rather than a second writer: synchronization is off under
/// this topology, so nothing it runs changes what this class stored.
/// </para>
/// <para>
/// One call, and one credential spent from a bucket this collection shares. The rate limit is what the sibling class
/// measures, so a class here that made a call per assertion would change what that one observes.
/// </para>
/// </remarks>
[Collection(ComposedHostCollectionDefinition.Name)]
public sealed class ComposedMcpToolContractTests(MailFathomOrchestrationFixture orchestration)
{
    private const string ToolName = "search_emails";

    private const string FolderAlias = "mcp-tool-contract";

    /// <summary>The word the seeded body carries and the query is written as, distinctive enough to select one message.</summary>
    private const string QueryTerm = "chartering";

    private const string SeededSubject = "mcp-tool-contract-seeded";

    /// <summary>
    /// The whole path a client takes: a call by the tool's published name, answered from the mail this deployment
    /// serves, in the fields the tool's own description names.
    /// </summary>
    [Fact]
    public async Task CallTool_SearchingForSeededMail_AnswersInTheShapeTheToolContractPublishes()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await this.SeedOneMessageAsync(cancellationToken);

        using var client = await orchestration.OpenMcpEndpointClientAsync(cancellationToken);
        using var request = SearchEmailsCall();

        // Act
        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var message = JsonDocument.Parse(JsonRpcMessage(body));
        var result = message.RootElement.GetProperty("result");

        // A protocol-level error is reported inside a successful result rather than as a status code, so a call that
        // failed would otherwise read as one that answered.
        Assert.False(
            result.TryGetProperty("isError", out var isError) && isError.GetBoolean(),
            $"The tool call was answered as an error: {body}");

        var structured = result.GetProperty("structuredContent");
        var matches = structured.GetProperty("matches");

        Assert.Equal(JsonValueKind.Array, matches.ValueKind);

        var seeded = Assert
            .Single(
                matches.EnumerateArray(),
                match => match.GetProperty("summary").GetProperty("subject").GetString() == SeededSubject)
            .GetProperty("summary");

        Assert.Equal(OrchestrationContract.ServedMailAccountId, seeded.GetProperty("accountId").GetString());
        Assert.Equal(FolderAlias, seeded.GetProperty("folderAlias").GetString());

        // The field the tool's description tells a client to read in order to know how the window was ranked. Its value
        // is the deployment's rather than this test's, so what is asserted is that it is published and populated.
        Assert.False(string.IsNullOrWhiteSpace(structured.GetProperty("retrievalMode").GetString()));
    }

    /// <summary>Stores one message the search reaches, through the production write path.</summary>
    private async Task SeedOneMessageAsync(CancellationToken cancellationToken)
    {
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid: 9801);

        var commitResult = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IEmailMetadataRepository>().UpsertMetadataAsync(
                session,
                SyntheticEmail.RemoteMetadataOf(occurrenceId, SeededSubject),
                SyntheticEmail.ExtractionOf(
                    occurrenceId,
                    SeededSubject,
                    SyntheticEmail.BodyTextContaining(QueryTerm, wordCount: 40),
                    "recipient@mailfathom.test"),
                StoredEmailContentAvailability.Available,
                token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);
    }

    /// <summary>Builds the JSON-RPC call a client makes, carrying the credential and the origin this host serves.</summary>
    /// <remarks>
    /// The folder is named in the arguments so the window holds this class's own message whatever else the suite left in
    /// the database, and naming it is itself part of what the call proves: an alias arriving as a wire argument has to
    /// reach the query as the scope it narrows.
    /// </remarks>
    private static HttpRequestMessage SearchEmailsCall()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/mcp", UriKind.Relative))
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/call",
                @params = new
                {
                    name = ToolName,
                    arguments = new
                    {
                        queryText = QueryTerm,
                        accountIds = new[] { OrchestrationContract.ServedMailAccountId },
                        folderAliases = new[] { FolderAlias },
                    },
                },
            }),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", OrchestrationContract.McpApiKey);
        request.Headers.Add("Origin", OrchestrationContract.McpPermittedOrigin);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        return request;
    }

    /// <summary>Reads the JSON-RPC message out of a body the transport may have framed as an event stream.</summary>
    /// <remarks>
    /// Which of its two content types the Streamable HTTP transport replies with is the transport's decision rather than
    /// this test's, so both are read. An event stream carries the message on a <c>data:</c> line; a JSON body is the
    /// message.
    /// </remarks>
    private static string JsonRpcMessage(string body)
    {
        const string EventStreamDataPrefix = "data:";

        var dataLine = body
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .FirstOrDefault(line => line.StartsWith(EventStreamDataPrefix, StringComparison.Ordinal));

        return dataLine is null ? body : dataLine[EventStreamDataPrefix.Length..].Trim();
    }
}
