// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text.Json;
using MailFathom.IntegrationTests.Orchestration;
using Xunit;

namespace MailFathom.IntegrationTests.Hosting;

/// <summary>Proves the contact tools over the real transport, from recording a person to erasing them again.</summary>
/// <remarks>
/// <para>
/// The tools' own conversions are covered against substitutes, and the book's rules against the orchestrated database.
/// What neither reaches is the path this class walks: that a write tool is advertised to a credential granted the whole
/// mail surface, that its arguments bind from the wire, that what one call returned is what the next call names the
/// person by, and that a record written through the protocol is in the same book a later read serves from.
/// </para>
/// <para>
/// One walk rather than a test per tool, because the four calls are one story and each one arranges the next: erasing a
/// person the same walk recorded is what makes the counts a fact about this class's own rows rather than about whatever
/// the suite left in the shared book. Four calls also stay well inside the credential's bucket, which the sibling class
/// measuring the limit spends against a credential of its own.
/// </para>
/// </remarks>
[Collection(ComposedHostCollectionDefinition.Name)]
public sealed class ComposedContactToolContractTests(MailFathomOrchestrationFixture orchestration)
{
    private const string DisplayName = "Composed Contact Walk";

    /// <summary>The address the walk records, distinctive so the search selects this class's own person.</summary>
    private const string Address = "composed-walk@contacts.mcp.test";

    /// <summary>The text the listing searches by, written in a casing neither the name nor the address uses.</summary>
    private const string SearchText = "COMPOSED-WALK@Contacts.Mcp.Test";

    /// <summary>The whole path: record a person, find them by that search, resolve them by address, and erase them.</summary>
    [Fact]
    public async Task ContactTools_RecordingFindingAndErasingOnePerson_AnswerInTheShapeTheirContractsPublish()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await orchestration.OpenMcpEndpointClientAsync(cancellationToken);

        // Act
        using var created = await CallAsync(
            client,
            "create_contact",
            new
            {
                displayName = DisplayName,
                addresses = new[] { Address },
                preferredAddress = Address,
                note = "Recorded by the composed contact walk.",
            },
            id: 1,
            cancellationToken);

        var recorded = ResultIn(created);
        var contactId = recorded.GetProperty("contact").GetProperty("contactId").GetString();

        using var listed = await CallAsync(
            client,
            "list_contacts",
            new { search = SearchText },
            id: 2,
            cancellationToken);

        using var resolved = await CallAsync(
            client,
            "get_contact",
            new { address = Address },
            id: 3,
            cancellationToken);

        using var erased = await CallAsync(
            client,
            "delete_contact",
            new { contactId },
            id: 4,
            cancellationToken);

        // Assert
        Assert.Equal("written", recorded.GetProperty("state").GetString());
        Assert.Equal(DisplayName, recorded.GetProperty("contact").GetProperty("displayName").GetString());
        Assert.Equal("asserted", recorded.GetProperty("contact").GetProperty("origin").GetString());

        // The search is answered by the deployment rather than narrowed by this test, so the person recorded a moment
        // ago has to be the one it selects — which is what says the write and the read reached one book.
        var found = Assert.Single(ResultIn(listed).GetProperty("contacts").EnumerateArray());

        Assert.Equal(contactId, found.GetProperty("contactId").GetString());

        Assert.Equal(contactId, ResultIn(resolved).GetProperty("contact").GetProperty("contactId").GetString());

        var erasure = ResultIn(erased);

        Assert.Equal(contactId, erasure.GetProperty("contactId").GetString());
        Assert.True(erasure.GetProperty("wasHeld").GetBoolean());
        Assert.Equal(1, erasure.GetProperty("addressesErased").GetInt32());
    }

    private static async Task<JsonDocument> CallAsync(
        HttpClient client,
        string toolName,
        object arguments,
        int id,
        CancellationToken cancellationToken)
    {
        using var request = McpToolCall.Of(toolName, arguments, id);
        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(McpToolCall.MessageIn(body));
    }

    /// <summary>Reads the structured answer, failing on a call the protocol reported as an error inside a success.</summary>
    private static JsonElement ResultIn(JsonDocument message)
    {
        var result = message.RootElement.GetProperty("result");

        Assert.False(
            result.TryGetProperty("isError", out var isError) && isError.GetBoolean(),
            $"The tool call was answered as an error: {message.RootElement}");

        return result.GetProperty("structuredContent");
    }
}
