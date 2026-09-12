// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Host.Configuration.Chat;
using MailFathom.Host.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Chat;

/// <summary>Covers what a reply-drafting declaration has to say before an instance will start on it.</summary>
/// <remarks>
/// Reached through the chat declaration that owns it rather than in isolation, because that is where an operator writes
/// it and because the one rule about it is that a drafting needs an endpoint to send a conversation to.
/// </remarks>
public sealed class ReplyDraftingOptionsTests
{
    /// <summary>On is the default wherever an endpoint is declared, so a deployment that wrote no block drafts replies.</summary>
    [Fact]
    public void Validate_AChatEndpointWithNoReplyDraftingBlock_IsAcceptedAndLeavesTheDraftingOn()
    {
        // Arrange
        var settings = Declared();

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.True(settings.ReplyDrafting.Enabled);
        Assert.Empty(errors);
    }

    /// <summary>Turning it off is a supported deployment, and it is the operator's spend decision rather than a lesser instance.</summary>
    [Fact]
    public void Validate_ADeclinedDraftingOnADeclaredEndpoint_IsAccepted()
    {
        // Arrange
        var settings = Declared();
        settings.ReplyDrafting.Enabled = false;

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A manner is derived wherever drafting is, because a reply that does not sound like its sender is one somebody rewrites.</summary>
    [Fact]
    public void Validate_AChatEndpointWithNoReplyDraftingBlock_LeavesTheMannerDerivedFromSentMail()
    {
        // Arrange
        var settings = Declared();

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.True(settings.ReplyDrafting.StyleFromSentMail);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_AnEnabledDraftingOnADeclaredEndpoint_IsAccepted()
    {
        // Arrange
        var settings = Declared();
        settings.ReplyDrafting.Enabled = true;

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>
    /// A drafting left on declares no provider, because on is what a section nobody wrote already reads: taking it as
    /// intent would refuse every deployment that never opened the block, which is most of them.
    /// </summary>
    [Fact]
    public void Validate_TheDraftingLeftOnWithNoChatEndpoint_DeclaresNoProvider()
    {
        // Arrange
        var settings = new ChatModelOptions();

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.False(settings.IsConfigured);
        Assert.True(settings.ReplyDrafting.Enabled);
        Assert.Empty(errors);
    }

    /// <summary>
    /// Declining the manner declares no provider, for the reason writing the sentence reading off declares none: it is
    /// an operator narrowing what a capability they may not even have would read, rather than asking for one.
    /// </summary>
    [Fact]
    public void Validate_TheMannerDeclinedWithNoChatEndpoint_DeclaresNoProvider()
    {
        // Arrange
        var settings = new ChatModelOptions();
        settings.ReplyDrafting.StyleFromSentMail = false;

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.False(settings.IsConfigured);
        Assert.Empty(errors);
    }

    private static ChatModelOptions Declared() => DeclaredChatModels.Section();

    private static IReadOnlyList<string> Validate(ChatModelOptions settings) =>
    [
        .. settings
            .Validate(new ValidationContext(settings))
            .Select(result => result.ErrorMessage ?? string.Empty),
    ];
}
