// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Mcp.Tools;
using MailFathom.Mcp.Tools.Contacts;
using MailFathom.Mcp.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools;

/// <summary>Covers the set this surface answers for: which names it publishes, and what reaching each one requires.</summary>
public sealed class PublishedToolsTests
{
    /// <summary>A tool published without a permission is one the surface could offer ungoverned, so the registration is what this is read against.</summary>
    [Fact]
    public void TryGetRequiredPermission_EveryToolTheRegistrationAdvertises_DeclaresAPermission()
    {
        // Arrange
        var advertisedToolNames = RegisteredMcpToolSurface.Tools()
            .Select(static tool => tool.ProtocolTool.Name)
            .ToArray();

        // Act
        var undeclared = advertisedToolNames
            .Where(static name => !PublishedTools.TryGetRequiredPermission(name, out _))
            .ToArray();

        // Assert
        Assert.Empty(undeclared);
    }

    /// <summary>A grant is written on the endpoint that serves the tool, so a permission belonging to the other half would sit in the file governing nothing.</summary>
    [Fact]
    public void TryGetRequiredPermission_EveryToolTheRegistrationAdvertises_DeclaresAPermissionOfTheMailSurface()
    {
        // Arrange
        var advertisedToolNames = RegisteredMcpToolSurface.Tools()
            .Select(static tool => tool.ProtocolTool.Name)
            .ToArray();

        // Act
        var surfaces = advertisedToolNames
            .Select(static name =>
            {
                PublishedTools.TryGetRequiredPermission(name, out var permission);

                return permission.Surface;
            })
            .Distinct()
            .ToArray();

        // Assert
        Assert.Equal([ProtectedSurface.Mail], surfaces);
    }

    /// <summary>The names the set answers for are the ones the registration advertises, so neither can gain an entry the other does not have.</summary>
    [Fact]
    public void Contains_TheNamesTheRegistrationAdvertises_AreTheOnesThisSetAnswersFor()
    {
        // Arrange
        var advertisedToolNames = RegisteredMcpToolSurface.Tools()
            .Select(static tool => tool.ProtocolTool.Name)
            .ToArray();

        // Act
        var recognized = advertisedToolNames.Where(PublishedTools.Contains).ToArray();

        // Assert
        Assert.Equal(advertisedToolNames, recognized);
    }

    [Theory]
    [InlineData(ListAccountsTool.ToolName)]
    [InlineData(ListEmailsTool.ToolName)]
    [InlineData(GetEmailContentTool.ToolName)]
    [InlineData(SearchEmailsTool.ToolName)]
    public void TryGetRequiredPermission_AToolThatReadsTheLocalCopy_RequiresTheMailboxReadGrant(string toolName)
    {
        // Act
        var declared = PublishedTools.TryGetRequiredPermission(toolName, out var permission);

        // Assert
        Assert.True(declared);
        Assert.Equal(MailFathomPermission.MailRead, permission);
    }

    /// <summary>Answering sends mail content to a model provider, which is a decision about egress rather than about reading.</summary>
    [Fact]
    public void TryGetRequiredPermission_TheAnsweringTool_RequiresTheAnsweringGrant()
    {
        // Act
        var declared = PublishedTools.TryGetRequiredPermission(AskMailTool.ToolName, out var permission);

        // Assert
        Assert.True(declared);
        Assert.Equal(MailFathomPermission.MailAsk, permission);
    }

    [Theory]
    [InlineData(ListContactsTool.ToolName)]
    [InlineData(GetContactTool.ToolName)]
    public void TryGetRequiredPermission_AToolThatReadsTheContactBook_RequiresTheContactReadingGrant(string toolName)
    {
        // Act
        var declared = PublishedTools.TryGetRequiredPermission(toolName, out var permission);

        // Assert
        Assert.True(declared);
        Assert.Equal(MailFathomPermission.MailContactsRead, permission);
    }

    /// <summary>Writing the book is separated from reading it because erasing a person is not something a reader's grant may reach.</summary>
    [Theory]
    [InlineData(CreateContactTool.ToolName)]
    [InlineData(UpdateContactTool.ToolName)]
    [InlineData(DeleteContactTool.ToolName)]
    [InlineData(PromoteContactTool.ToolName)]
    public void TryGetRequiredPermission_AToolThatWritesTheContactBook_RequiresTheContactWritingGrant(string toolName)
    {
        // Act
        var declared = PublishedTools.TryGetRequiredPermission(toolName, out var permission);

        // Assert
        Assert.True(declared);
        Assert.Equal(MailFathomPermission.MailContactsWrite, permission);
    }

    /// <summary>Writing a mailbox is a grant of its own, and this mapping is what withholds the tool from a reader.</summary>
    /// <remarks>
    /// The use case checks the same permission again, so a credential holding only <c>mailfathom.mail.read</c> is
    /// refused whatever this says. What this decides is the half a refusal cannot repair: a mapping naming a read grant
    /// would advertise the mailbox-writing tool to that credential and let it call one, which is disclosure and an
    /// offer against least privilege rather than a change that got through.
    /// </remarks>
    [Fact]
    public void TryGetRequiredPermission_TheToolThatWritesAMailbox_RequiresTheFlagWritingGrant()
    {
        // Act
        var declared = PublishedTools.TryGetRequiredPermission(SetMailFlagsTool.ToolName, out var permission);

        // Assert
        Assert.True(declared);
        Assert.Equal(MailFathomPermission.MailFlagsWrite, permission);
    }

    /// <summary>Sending is the one grant whose effect leaves the deployment, and this mapping is what withholds the tool from everyone else.</summary>
    /// <remarks>
    /// A mapping naming any weaker grant would offer <c>send_email</c> to a credential the operator gave a mailbox to
    /// read or to write flags on, and the offer alone is the defect: the use case refuses the call, but the descriptor
    /// has already told a client that this deployment will send mail on that credential's behalf.
    /// </remarks>
    [Fact]
    public void TryGetRequiredPermission_TheToolThatSendsMail_RequiresTheSendGrant()
    {
        // Act
        var declared = PublishedTools.TryGetRequiredPermission(SendEmailTool.ToolName, out var permission);

        // Assert
        Assert.True(declared);
        Assert.Equal(MailFathomPermission.MailSend, permission);
    }

    /// <summary>An answer to stored mail is a send, so it is withheld from every credential the send grant was not written for.</summary>
    /// <remarks>
    /// The use case beneath asks for the reading grant as well, because an answer quotes the message it answers — but
    /// the listing is narrowed by the sending one, since that is the grant whose absence must hide the tool rather than
    /// merely refuse the call. A credential holding one and not the other meets the use case's refusal, which is the
    /// deployment's own grant to correct.
    /// </remarks>
    [Theory]
    [InlineData(ReplyToEmailTool.ToolName)]
    [InlineData(ForwardEmailTool.ToolName)]
    public void TryGetRequiredPermission_AToolThatAnswersStoredMail_RequiresTheSendGrant(string toolName)
    {
        // Act
        var declared = PublishedTools.TryGetRequiredPermission(toolName, out var permission);

        // Assert
        Assert.True(declared);
        Assert.Equal(MailFathomPermission.MailSend, permission);
    }

    /// <summary>Reading back what a caller queued, and stopping it, are part of sending rather than part of reading a mailbox.</summary>
    /// <remarks>
    /// A mapping naming the reading grant would offer a credential given a mailbox to read a tool that reports who this
    /// mailbox wrote to and when, which is the disclosure the absence of a listing exists to prevent — reached one
    /// identifier at a time instead of in a page.
    /// </remarks>
    [Theory]
    [InlineData(GetOutgoingEmailTool.ToolName)]
    [InlineData(CancelOutgoingEmailTool.ToolName)]
    public void TryGetRequiredPermission_AToolOverAQueuedSend_RequiresTheSendGrant(string toolName)
    {
        // Act
        var declared = PublishedTools.TryGetRequiredPermission(toolName, out var permission);

        // Assert
        Assert.True(declared);
        Assert.Equal(MailFathomPermission.MailSend, permission);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("List_Accounts")]
    [InlineData("delete_everything")]
    public void TryGetRequiredPermission_ANameNoToolAnswersTo_DeclaresNothing(string? toolName)
    {
        // Act
        var declared = PublishedTools.TryGetRequiredPermission(toolName, out var permission);

        // Assert
        Assert.False(declared);
        Assert.False(permission.IsSpecified);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("List_Accounts")]
    [InlineData("delete_everything")]
    public void Contains_ANameNoToolAnswersTo_IsNotPublished(string? toolName)
    {
        // Act, Assert
        Assert.False(PublishedTools.Contains(toolName));
    }

    /// <summary>A published name is what a signal about the call carries, because the closed set bounds the series it opens.</summary>
    [Fact]
    public void MeasurableName_APublishedTool_IsTheNameItself()
    {
        // Act, Assert
        Assert.Equal(SearchEmailsTool.ToolName, PublishedTools.MeasurableName(SearchEmailsTool.ToolName));
    }

    /// <summary>Anything else is one fixed value, so a client looping over names it invented mints no series apiece.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("List_Accounts")]
    [InlineData("delete_everything")]
    public void MeasurableName_ANameNoToolAnswersTo_IsTheOnePlaceholder(string? toolName)
    {
        // Act, Assert
        Assert.Equal(PublishedTools.UnpublishedToolName, PublishedTools.MeasurableName(toolName));
    }
}
