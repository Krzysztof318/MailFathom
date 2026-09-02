// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Mcp.Tools;
using MailFathom.Mcp.Tools.Contacts;
using MailFathom.Mcp.Tools.Drafts;
using MailFathom.Mcp.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools;

/// <summary>Covers which tools the registration composes, which is the published surface rather than any one contract.</summary>
/// <remarks>
/// It belongs to no single tool, so it lives on its own: the set is what a client discovers, and a tool arriving in it
/// unnoticed is a change to the published contract whichever family it joins. What each of them then requires is
/// <see cref="PublishedToolsTests" />, and which of them a particular caller is listed is
/// <see cref="McpToolSurfaceCompositionTests" />.
/// </remarks>
public sealed class RegisteredToolSurfaceTests
{
    /// <summary>The mailbox tools, the draft tools, and the contact tools of this release, so a twenty-second arriving unnoticed is a change to the published contract.</summary>
    /// <remarks>
    /// Registration is not advertisement. <c>ask_mail</c> is registered by every deployment and listed only by one that
    /// can answer, and every tool here is listed only to a caller whose grant reaches it — so this set is the ceiling a
    /// listing is drawn from rather than a listing anybody receives.
    /// </remarks>
    [Fact]
    public void AddMailFathomServer_RegistersTheMailboxToolsAndTheContactTools()
    {
        // Arrange, Act
        var registeredNames = RegisteredMcpToolSurface
            .Tools()
            .Select(tool => tool.ProtocolTool.Name)
            .Order(StringComparer.Ordinal);

        // Assert
        Assert.Equal(
            [
                AskMailTool.ToolName,
                CancelOutgoingEmailTool.ToolName,
                CreateContactTool.ToolName,
                DeleteContactTool.ToolName,
                DeleteDraftTool.ToolName,
                ForwardEmailTool.ToolName,
                GetContactTool.ToolName,
                GetEmailContentTool.ToolName,
                GetOutgoingEmailTool.ToolName,
                ListAccountsTool.ToolName,
                ListContactsTool.ToolName,
                ListEmailsTool.ToolName,
                PromoteContactTool.ToolName,
                ReplyToEmailTool.ToolName,
                SaveDraftTool.ToolName,
                SearchEmailsTool.ToolName,
                SendDraftTool.ToolName,
                SendEmailTool.ToolName,
                SetMailFlagsTool.ToolName,
                UpdateContactTool.ToolName,
                UpdateDraftTool.ToolName,
            ],
            registeredNames);
    }
}
