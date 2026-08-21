// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Frozen;
using MailFathom.Domain.Access;
using MailFathom.Mcp.Tools.Categories;
using MailFathom.Mcp.Tools.Contacts;
using MailFathom.Mcp.Tools.Drafts;

namespace MailFathom.Mcp.Tools;

/// <summary>The tools this surface publishes, as one closed set known at build time, with what each declares about itself.</summary>
/// <remarks>
/// <para>
/// A caller chooses the name it sends, so the name that arrives with a request is unvalidated input until something says
/// whether a tool answers to it. That distinction is what this set exists for: a dimension whose values a caller picks is
/// a time series per value, and a client looping over misspelled names would open one apiece.
/// </para>
/// <para>
/// Every half is read from the tools themselves rather than restated, so a tool renamed, regranted, or recategorized in
/// the one place it is declared cannot leave a stale literal here that keeps matching nothing. A tool that reaches this
/// surface without an entry is a tool nothing can answer for, which is why every caller of
/// <see cref="TryGetRequiredPermission" /> and of <see cref="TryGetCategory" /> treats an absent answer as a refusal
/// rather than as a tool nobody bounded — and why <see cref="PublishedToolCategoryGate" /> refuses to start a host
/// serving one.
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

    private static readonly FrozenDictionary<string, PublishedTool> DeclarationsByName =
        new Dictionary<string, PublishedTool>(StringComparer.Ordinal)
        {
            [ListAccountsTool.ToolName] = new(ListAccountsTool.RequiredPermission, ListAccountsTool.Category),
            [ListEmailsTool.ToolName] = new(ListEmailsTool.RequiredPermission, ListEmailsTool.Category),
            [GetEmailContentTool.ToolName] = new(GetEmailContentTool.RequiredPermission, GetEmailContentTool.Category),
            [SearchEmailsTool.ToolName] = new(SearchEmailsTool.RequiredPermission, SearchEmailsTool.Category),
            [SetMailFlagsTool.ToolName] = new(SetMailFlagsTool.RequiredPermission, SetMailFlagsTool.Category),
            [SendEmailTool.ToolName] = new(SendEmailTool.RequiredPermission, SendEmailTool.Category),
            [ReplyToEmailTool.ToolName] = new(ReplyToEmailTool.RequiredPermission, ReplyToEmailTool.Category),
            [ForwardEmailTool.ToolName] = new(ForwardEmailTool.RequiredPermission, ForwardEmailTool.Category),
            [SaveDraftTool.ToolName] = new(SaveDraftTool.RequiredPermission, SaveDraftTool.Category),
            [UpdateDraftTool.ToolName] = new(UpdateDraftTool.RequiredPermission, UpdateDraftTool.Category),
            [DeleteDraftTool.ToolName] = new(DeleteDraftTool.RequiredPermission, DeleteDraftTool.Category),
            [SendDraftTool.ToolName] = new(SendDraftTool.RequiredPermission, SendDraftTool.Category),
            [GetOutgoingEmailTool.ToolName] = new(GetOutgoingEmailTool.RequiredPermission, GetOutgoingEmailTool.Category),
            [CancelOutgoingEmailTool.ToolName] = new(CancelOutgoingEmailTool.RequiredPermission, CancelOutgoingEmailTool.Category),
            [AskMailTool.ToolName] = new(AskMailTool.RequiredPermission, AskMailTool.Category),
            [ListContactsTool.ToolName] = new(ListContactsTool.RequiredPermission, ListContactsTool.Category),
            [GetContactTool.ToolName] = new(GetContactTool.RequiredPermission, GetContactTool.Category),
            [CreateContactTool.ToolName] = new(CreateContactTool.RequiredPermission, CreateContactTool.Category),
            [UpdateContactTool.ToolName] = new(UpdateContactTool.RequiredPermission, UpdateContactTool.Category),
            [PromoteContactTool.ToolName] = new(PromoteContactTool.RequiredPermission, PromoteContactTool.Category),
            [DeleteContactTool.ToolName] = new(DeleteContactTool.RequiredPermission, DeleteContactTool.Category),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Determines whether a name a caller sent is one of the tools this surface publishes.</summary>
    /// <param name="toolName">The name the request carried, which may be absent or anything at all.</param>
    /// <returns><see langword="true" /> when a published tool answers to that name.</returns>
    public static bool Contains(string? toolName) =>
        toolName is not null && DeclarationsByName.ContainsKey(toolName);

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
        if (toolName is not null && DeclarationsByName.TryGetValue(toolName, out var declaration))
        {
            requiredPermission = declaration.RequiredPermission;

            return true;
        }

        requiredPermission = default;

        return false;
    }

    /// <summary>Reports the kind of thing a tool is for, which is what a deployment's category selection decides by.</summary>
    /// <param name="toolName">The name the request or the descriptor carried.</param>
    /// <param name="category">The category the tool declared, or the unspecified default when no tool answers to that name.</param>
    /// <returns><see langword="true" /> when a published tool answers to that name and therefore declared a category.</returns>
    /// <remarks>Absence is one answer rather than two, as it is for a permission: a tool nothing here knows about and a tool that stated no category are the same fact to whoever has to decide whether this endpoint offers it, so neither is published.</remarks>
    public static bool TryGetCategory(string? toolName, out McpToolCategory category)
    {
        if (toolName is not null && DeclarationsByName.TryGetValue(toolName, out var declaration))
        {
            category = declaration.Category;

            return true;
        }

        category = default;

        return false;
    }
}
