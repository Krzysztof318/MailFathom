// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Frozen;
using MailFathom.Domain.Access;
using MailFathom.Mcp.Tools.Contacts;
using MailFathom.Mcp.Tools.Drafts;

namespace MailFathom.Mcp.Tools;

/// <summary>The tools this surface publishes, as one closed set known at build time, with the permission each requires.</summary>
/// <remarks>
/// <para>
/// A caller chooses the name it sends, so the name that arrives with a request is unvalidated input until something says
/// whether a tool answers to it. That distinction is what this set exists for: a dimension whose values a caller picks is
/// a time series per value, and a client looping over misspelled names would open one apiece.
/// </para>
/// <para>
/// Both halves are read from the tools themselves rather than restated, so a tool renamed or regranted in the one place
/// it is declared cannot leave a stale literal here that keeps matching nothing. A tool that reaches this surface without
/// an entry is a tool nothing can answer for, which is why every caller of
/// <see cref="TryGetRequiredPermission" /> treats an absent answer as a refusal rather than as a tool nobody bounded.
/// </para>
/// </remarks>
internal static class PublishedTools
{
    /// <summary>The one name every tool this surface does not publish is reported under.</summary>
    /// <remarks>
    /// A signal about a call names the tool only where a published tool answers to it, and this otherwise. That is what
    /// separates a dimension from a log line, where the same name is recorded whenever its shape is safe: a log line
    /// costs what it is written to, and a dimension costs a time series that never goes away, so a client calling
    /// <c>list_email</c> in a loop must not be able to mint one apiece.
    /// </remarks>
    internal const string UnpublishedToolName = "(unpublished)";

    private static readonly FrozenDictionary<string, MailFathomPermission> RequiredPermissionsByName =
        new Dictionary<string, MailFathomPermission>(StringComparer.Ordinal)
        {
            [ListAccountsTool.ToolName] = ListAccountsTool.RequiredPermission,
            [ListEmailsTool.ToolName] = ListEmailsTool.RequiredPermission,
            [GetEmailContentTool.ToolName] = GetEmailContentTool.RequiredPermission,
            [SearchEmailsTool.ToolName] = SearchEmailsTool.RequiredPermission,
            [SetMailFlagsTool.ToolName] = SetMailFlagsTool.RequiredPermission,
            [SendEmailTool.ToolName] = SendEmailTool.RequiredPermission,
            [ReplyToEmailTool.ToolName] = ReplyToEmailTool.RequiredPermission,
            [ForwardEmailTool.ToolName] = ForwardEmailTool.RequiredPermission,
            [SaveDraftTool.ToolName] = SaveDraftTool.RequiredPermission,
            [UpdateDraftTool.ToolName] = UpdateDraftTool.RequiredPermission,
            [DeleteDraftTool.ToolName] = DeleteDraftTool.RequiredPermission,
            [SendDraftTool.ToolName] = SendDraftTool.RequiredPermission,
            [GetOutgoingEmailTool.ToolName] = GetOutgoingEmailTool.RequiredPermission,
            [CancelOutgoingEmailTool.ToolName] = CancelOutgoingEmailTool.RequiredPermission,
            [AskMailTool.ToolName] = AskMailTool.RequiredPermission,
            [ListContactsTool.ToolName] = ListContactsTool.RequiredPermission,
            [GetContactTool.ToolName] = GetContactTool.RequiredPermission,
            [CreateContactTool.ToolName] = CreateContactTool.RequiredPermission,
            [UpdateContactTool.ToolName] = UpdateContactTool.RequiredPermission,
            [PromoteContactTool.ToolName] = PromoteContactTool.RequiredPermission,
            [DeleteContactTool.ToolName] = DeleteContactTool.RequiredPermission,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Determines whether a name a caller sent is one of the tools this surface publishes.</summary>
    /// <param name="toolName">The name the request carried, which may be absent or anything at all.</param>
    /// <returns><see langword="true" /> when a published tool answers to that name.</returns>
    public static bool Contains(string? toolName) =>
        toolName is not null && RequiredPermissionsByName.ContainsKey(toolName);

    /// <summary>Reduces a name a caller sent to one this surface publishes, so it is safe to make a dimension of.</summary>
    /// <param name="toolName">The name the request carried, which may be absent or anything at all.</param>
    /// <returns>The name itself where a published tool answers to it, and <see cref="UnpublishedToolName" /> otherwise.</returns>
    /// <remarks>It lives here rather than beside a publisher because the closed set is what decides the answer, and two publishers reducing the same name apart would let one of them drift into measuring what a caller wrote.</remarks>
    public static string MeasurableName(string? toolName) => Contains(toolName) ? toolName! : UnpublishedToolName;

    /// <summary>Reports the permission a caller must hold to be offered a tool and to call it.</summary>
    /// <param name="toolName">The name the request or the descriptor carried.</param>
    /// <param name="requiredPermission">The permission the tool declared, or the unspecified default when no tool answers to that name.</param>
    /// <returns><see langword="true" /> when a published tool answers to that name and therefore declared a permission.</returns>
    /// <remarks>Absence is one answer rather than two, because a tool nothing here knows about and a tool that stated no permission are the same fact to whoever has to decide whether a caller may reach it: nobody decided, so nobody may.</remarks>
    public static bool TryGetRequiredPermission(string? toolName, out MailFathomPermission requiredPermission)
    {
        if (toolName is not null && RequiredPermissionsByName.TryGetValue(toolName, out requiredPermission))
        {
            return true;
        }

        requiredPermission = default;

        return false;
    }
}
