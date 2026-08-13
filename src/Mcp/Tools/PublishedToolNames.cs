// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Frozen;

namespace MailFathom.Mcp.Tools;

/// <summary>The tool names this surface advertises, as one closed set known at build time.</summary>
/// <remarks>
/// <para>
/// A caller chooses the name it sends, so the name that arrives with a request is unvalidated input until something
/// says whether a tool answers to it. That distinction is what this set exists for: a dimension whose values a caller
/// picks is a time series per value, and a client looping over misspelled names would open one apiece.
/// </para>
/// <para>
/// The names are read from the tools themselves rather than restated, so a tool renamed in the one place it is declared
/// cannot leave a stale literal here that keeps matching nothing.
/// </para>
/// </remarks>
internal static class PublishedToolNames
{
    private static readonly FrozenSet<string> Names = new[]
    {
        ListAccountsTool.ToolName,
        ListEmailsTool.ToolName,
        GetEmailContentTool.ToolName,
        SearchEmailsTool.ToolName,
        AskMailTool.ToolName,
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Determines whether a name a caller sent is one of the tools this surface publishes.</summary>
    /// <param name="toolName">The name the request carried, which may be absent or anything at all.</param>
    /// <returns><see langword="true" /> when a published tool answers to that name.</returns>
    public static bool Contains(string? toolName) => toolName is not null && Names.Contains(toolName);
}
