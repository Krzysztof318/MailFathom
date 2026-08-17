// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text.Json;
using MailFathom.AppHost;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
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

    /// <summary>The folder this class seeds into, which is the one the composed host's own configuration maps.</summary>
    /// <remarks>
    /// Taken from the app model rather than spelled again, because the two have to be the same folder: what makes the
    /// seeded mail readable through a tool is the host's mapping, and a class naming a folder that host does not map
    /// would read as the tool answering wrongly rather than as the mail being outside its scope.
    /// </remarks>
    private const string FolderAlias = OrchestrationContract.ComposedHostReadableFolderAlias;

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

        using var message = JsonDocument.Parse(McpToolCall.MessageIn(body));
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

        // Named by its display name on the way in and reported by both names on the way out, which is the whole of what
        // the two spellings promise a client: either selects the mailbox, and the identifier is what later results are
        // matched against.
        Assert.Equal(OrchestrationContract.ServedMailAccountId, seeded.GetProperty("accountId").GetString());
        Assert.Equal(
            OrchestrationContract.ServedMailAccountDisplayName,
            seeded.GetProperty("accountDisplayName").GetString());

        // The canonical alias rather than the argument's own spelling: an alias is normalized when it is created, and
        // what the tool publishes is the value a client matches later results against. Asserting the literal sent on
        // the wire would say a request's casing survives into a response, which is the opposite of the contract.
        Assert.Equal(
            MailFolderAlias.Create(FolderAlias).Value,
            seeded.GetProperty("folderAlias").GetString());

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

    /// <summary>Builds the JSON-RPC call a client makes.</summary>
    /// <remarks>
    /// The folder is named in the arguments so the window holds this class's own message whatever else the suite left in
    /// the database, and naming it is itself part of what the call proves: an alias arriving as a wire argument has to
    /// reach the query as the scope it narrows.
    /// </remarks>
    private static HttpRequestMessage SearchEmailsCall() => McpToolCall.Of(
        ToolName,
        new
        {
            queryText = QueryTerm,
            accounts = new[] { OrchestrationContract.ServedMailAccountDisplayName },
            folderAliases = new[] { FolderAlias },
        });
}
