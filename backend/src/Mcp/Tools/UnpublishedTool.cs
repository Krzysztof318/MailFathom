// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using ModelContextProtocol;

namespace MailFathom.Mcp.Tools;

/// <summary>The one answer a call naming a tool this surface will not serve receives, whatever the reason was.</summary>
/// <remarks>
/// <para>
/// A tool may go unserved because the caller's grant does not reach it, because this deployment publishes no such
/// category, or because no tool answers to the name at all. From the caller's side those are one fact, and a refusal it
/// could tell apart would disclose exactly what the listing withheld — so the answer is written once here rather than
/// once per filter, where two of them could drift into being distinguishable.
/// </para>
/// <para>
/// The wording is copied from the SDK's own answer to an unknown tool, because it publishes no member to reach that
/// answer through. Nothing verifies the two still match: the unit tests compare what this produces against a literal of
/// their own rather than against the SDK, so a release that reworded its message would leave the suite green. Reaching
/// the SDK's own dispatch needs a composed host over a real transport, which is the integration suite rather than a
/// unit test. What that drift would cost is a divergence from the server's default phrasing and nothing more, because
/// every refusal on this surface is decided in a filter and the SDK's unknown-tool path is not reachable through this
/// pipeline.
/// </para>
/// </remarks>
internal static class UnpublishedTool
{
    /// <summary>Writes the refusal a call naming an unserved tool is answered with.</summary>
    /// <param name="toolName">The name the request carried, which may be absent or anything at all.</param>
    /// <returns>The protocol failure to raise, naming nothing about the caller, the grant, or the deployment's selection.</returns>
    public static McpProtocolException Refusal(string? toolName) =>
        new($"Unknown tool: '{toolName}'", McpErrorCode.InvalidParams);
}
