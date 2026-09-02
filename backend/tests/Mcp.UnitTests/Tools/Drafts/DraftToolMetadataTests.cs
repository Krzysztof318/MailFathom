// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Mcp.Tools;
using MailFathom.Mcp.Tools.Drafts;
using MailFathom.Mcp.UnitTests.TestDoubles;
using ModelContextProtocol.Protocol;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools.Drafts;

/// <summary>Covers the descriptors MailFathom advertises for the four tools over a draft.</summary>
/// <remarks>
/// <para>
/// The four are worth asserting together because between them they publish the shapes this surface has no other pair
/// for: a write that creates, is not destructive, and is not idempotent, beside a send that is all three of the things
/// a send is. A client decides whether a call needs a person from these values, and the difference between saving a
/// draft and sending one is the whole reason both exist.
/// </para>
/// <para>
/// The descriptions are asserted as well, because a description is the safety surface a model reads before deciding to
/// call: which of these tools causes mail to leave is stated in words rather than left to the annotations.
/// </para>
/// </remarks>
public sealed class DraftToolMetadataTests
{
    [Fact]
    public void AddMailFathomServer_AdvertisesTheFourDraftToolsUnderTheirProtocolNames()
    {
        // Arrange, Act
        var save = AdvertisedTool(SaveDraftTool.ToolName);
        var update = AdvertisedTool(UpdateDraftTool.ToolName);
        var delete = AdvertisedTool(DeleteDraftTool.ToolName);
        var send = AdvertisedTool(SendDraftTool.ToolName);

        // Assert
        Assert.Equal("save_draft", save.Name);
        Assert.Equal("Save draft", save.Title);
        Assert.Equal("update_draft", update.Name);
        Assert.Equal("Update draft", update.Title);
        Assert.Equal("delete_draft", delete.Name);
        Assert.Equal("Delete draft", delete.Title);
        Assert.Equal("send_draft", send.Name);
        Assert.Equal("Send draft", send.Title);
    }

    /// <summary>A write that creates, leaves two drafts when it is called twice, and reaches the owner's own server and nobody else.</summary>
    /// <remarks>
    /// <c>destructiveHint</c> is <see langword="false" /> because saving a draft takes nothing away and one call
    /// undoes it, and <c>idempotentHint</c> is <see langword="false" /> because there is no idempotency key: two calls
    /// are two drafts. <c>openWorldHint</c> is <see langword="true" /> all the same, and for a reason worth pinning —
    /// the copy is appended to a mail server this deployment does not own, which is the flag's question rather than
    /// whether a third party is reached.
    /// </remarks>
    [Fact]
    public void AddMailFathomServer_AdvertisesSavingADraftAsACreatingNonIdempotentOpenWorldWrite()
    {
        // Arrange, Act
        var annotations = AdvertisedTool(SaveDraftTool.ToolName).Annotations;

        // Assert
        Assert.NotNull(annotations);
        Assert.False(annotations.ReadOnlyHint);
        Assert.False(annotations.DestructiveHint);
        Assert.False(annotations.IdempotentHint);
        Assert.True(annotations.OpenWorldHint);
    }

    /// <summary>An edit states the whole message, so it drops what the caller left out and a second identical call changes nothing further.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesUpdatingADraftAsADestructiveIdempotentWrite()
    {
        // Arrange, Act
        var annotations = AdvertisedTool(UpdateDraftTool.ToolName).Annotations;

        // Assert
        Assert.NotNull(annotations);
        Assert.False(annotations.ReadOnlyHint);
        Assert.True(annotations.DestructiveHint);
        Assert.True(annotations.IdempotentHint);
        Assert.True(annotations.OpenWorldHint);
    }

    /// <summary>Deleting is destructive in the plain sense and idempotent because a second call asks for the state the first one left.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesDeletingADraftAsDestructiveAndIdempotent()
    {
        // Arrange, Act
        var annotations = AdvertisedTool(DeleteDraftTool.ToolName).Annotations;

        // Assert
        Assert.NotNull(annotations);
        Assert.False(annotations.ReadOnlyHint);
        Assert.True(annotations.DestructiveHint);
        Assert.True(annotations.IdempotentHint);
        Assert.True(annotations.OpenWorldHint);
    }

    /// <summary>Sending a draft is a send, so it carries a send's annotations exactly.</summary>
    /// <remarks>
    /// Asserted against <c>send_email</c>'s own descriptor rather than against four literals, because the claim is that
    /// the two are annotated identically: a tool that sends real mail is not made safer by the message having been
    /// written down first.
    /// </remarks>
    [Fact]
    public void AddMailFathomServer_AdvertisesSendingADraftWithTheAnnotationsOfSendEmail()
    {
        // Arrange, Act
        var draft = AdvertisedTool(SendDraftTool.ToolName).Annotations;
        var email = AdvertisedTool(SendEmailTool.ToolName).Annotations;

        // Assert
        Assert.NotNull(draft);
        Assert.NotNull(email);
        Assert.Equal(email.ReadOnlyHint, draft.ReadOnlyHint);
        Assert.Equal(email.DestructiveHint, draft.DestructiveHint);
        Assert.Equal(email.IdempotentHint, draft.IdempotentHint);
        Assert.Equal(email.OpenWorldHint, draft.OpenWorldHint);
    }

    /// <summary>The three tools that send nothing say so in the words a model reads before deciding to call.</summary>
    [Theory]
    [InlineData(SaveDraftTool.ToolName)]
    [InlineData(UpdateDraftTool.ToolName)]
    [InlineData(DeleteDraftTool.ToolName)]
    public void AddMailFathomServer_AdvertisesADescriptionStatingThatTheDraftingToolsSendNothing(string toolName)
    {
        // Arrange, Act
        var description = AdvertisedTool(toolName).Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("nothing", description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sends a real email", description, StringComparison.Ordinal);
    }

    /// <summary>Saving a draft is the safe half, and the description names the tool that is not.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesADescriptionStatingThatSavingSendsNothingAndNamingWhatDoes()
    {
        // Arrange, Act
        var description = AdvertisedTool(SaveDraftTool.ToolName).Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("SENDS NOTHING", description, StringComparison.Ordinal);
        Assert.Contains("send_draft", description, StringComparison.Ordinal);
        Assert.Contains("two drafts", description, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The one draft tool that causes mail to leave says so as plainly as <c>send_email</c> does.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesADescriptionStatingThatSendingADraftCannotBeRecalled()
    {
        // Arrange, Act
        var description = AdvertisedTool(SendDraftTool.ToolName).Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("Sends a real email", description, StringComparison.Ordinal);
        Assert.Contains("CANNOT be recalled", description, StringComparison.Ordinal);
        Assert.Contains("The call itself transmits nothing", description, StringComparison.Ordinal);
        Assert.Contains("the result says queued", description, StringComparison.Ordinal);
    }

    /// <summary>An edit replaces the message, which is the one thing a caller must not learn from the owner.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesADescriptionStatingThatAnUpdateStatesTheWholeMessage()
    {
        // Arrange, Act
        var description = AdvertisedTool(UpdateDraftTool.ToolName).Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("WHOLE message", description, StringComparison.Ordinal);
        Assert.Contains("Replaces", description, StringComparison.Ordinal);
    }

    /// <summary>The three that write a draft are behind the drafting grant, and the one that sends is behind the sending grant.</summary>
    [Fact]
    public void PublishedTools_TheDraftTools_DeclareTheGrantEachActuallyNeeds()
    {
        // Arrange, Act
        var required = new[]
        {
            SaveDraftTool.ToolName,
            UpdateDraftTool.ToolName,
            DeleteDraftTool.ToolName,
            SendDraftTool.ToolName,
        }.Select(name =>
        {
            PublishedTools.TryGetRequiredPermission(name, out var permission);

            return permission;
        });

        // Assert
        Assert.Equal(
            [
                MailFathomPermission.MailDraftsWrite,
                MailFathomPermission.MailDraftsWrite,
                MailFathomPermission.MailDraftsWrite,
                MailFathomPermission.MailSend,
            ],
            required);
    }

    /// <summary>A draft's identity is what the four exchange, and none of them publishes a word of the message.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesAnAnswerCarryingNothingAboutTheMessageItself()
    {
        // Arrange, Act
        var outputSchema = AdvertisedTool(SaveDraftTool.ToolName).OutputSchema;

        // Assert
        Assert.NotNull(outputSchema);
        var advertisedSchema = outputSchema.Value.GetRawText();
        Assert.Contains("\"draftId\"", advertisedSchema, StringComparison.Ordinal);
        Assert.Contains("\"recipientCount\"", advertisedSchema, StringComparison.Ordinal);
        Assert.DoesNotContain("\"subject\"", advertisedSchema, StringComparison.Ordinal);
        Assert.DoesNotContain("\"plainTextBody\"", advertisedSchema, StringComparison.Ordinal);
        Assert.DoesNotContain("\"htmlBody\"", advertisedSchema, StringComparison.Ordinal);
        Assert.DoesNotContain("\"recipients\"", advertisedSchema, StringComparison.Ordinal);
    }

    /// <summary>The answer a promoted draft produces is the send's own, so a caller reads one shape whichever way it queued a message.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheSameAnswerForSendingADraftAndSendingAMessage()
    {
        // Arrange, Act
        var draft = AdvertisedTool(SendDraftTool.ToolName).OutputSchema;
        var email = AdvertisedTool(SendEmailTool.ToolName).OutputSchema;

        // Assert
        Assert.NotNull(draft);
        Assert.NotNull(email);
        Assert.Equal(email.Value.GetRawText(), draft.Value.GetRawText());
    }

    /// <summary>Which answer a drafted reply is has no default, so the argument is advertised with the three the surface publishes.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheThreeAnswersADraftMayBe()
    {
        // Arrange, Act
        var inputSchema = AdvertisedTool(SaveDraftTool.ToolName).InputSchema.GetRawText();

        // Assert
        Assert.Contains("\"senderOnly\"", inputSchema, StringComparison.Ordinal);
        Assert.Contains("\"everyone\"", inputSchema, StringComparison.Ordinal);
        Assert.Contains("\"forward\"", inputSchema, StringComparison.Ordinal);
    }

    private static Tool AdvertisedTool(string toolName) => RegisteredMcpToolSurface.AdvertisedTool(toolName);
}
