// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net.Http.Headers;
using System.Net.Http.Json;
using MailFathom.AppHost;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>Builds a tool call against the composed host's MCP endpoint, and reads the message that comes back.</summary>
/// <remarks>
/// Shared because both halves are the transport's shape rather than any one test's: the credential and the origin this
/// host serves are what get a call through the controls in front of the endpoint, and which of its two content types the
/// Streamable HTTP transport replies with is the transport's decision. A class asserting what a tool answers restates
/// neither. A class about the controls themselves builds its own request, since varying a header is what it is for.
/// </remarks>
internal static class McpToolCall
{
    private const string EventStreamDataPrefix = "data:";

    /// <summary>Builds the JSON-RPC request that calls one tool.</summary>
    /// <param name="toolName">The name the tool is advertised under.</param>
    /// <param name="arguments">The arguments object, serialized as the call's <c>arguments</c>.</param>
    /// <param name="id">The JSON-RPC identifier, which a class making several calls varies.</param>
    /// <returns>The request, which the caller sends and disposes.</returns>
    public static HttpRequestMessage Of(string toolName, object arguments, int id = 1)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/mcp", UriKind.Relative))
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id,
                method = "tools/call",
                @params = new { name = toolName, arguments },
            }),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", OrchestrationContract.McpApiKey);
        request.Headers.Add("Origin", OrchestrationContract.McpPermittedOrigin);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        return request;
    }

    /// <summary>Reads the JSON-RPC message out of a body the transport may have framed as an event stream.</summary>
    /// <param name="body">The response body as it arrived.</param>
    /// <returns>The JSON-RPC message.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body" /> is <see langword="null" />.</exception>
    /// <remarks>An event stream carries the message on a <c>data:</c> line; a JSON body is the message.</remarks>
    public static string MessageIn(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var dataLine = body
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .FirstOrDefault(line => line.StartsWith(EventStreamDataPrefix, StringComparison.Ordinal));

        return dataLine is null ? body : dataLine[EventStreamDataPrefix.Length..].Trim();
    }
}
