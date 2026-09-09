// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Host.Configuration.Chat;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Chat;

/// <summary>Covers what a reply-drafting declaration has to say before an instance will start on it.</summary>
/// <remarks>
/// Reached through the chat declaration that owns it rather than in isolation, because that is where an operator writes
/// it and because the one rule about it is that a drafting needs an endpoint to send a conversation to.
/// </remarks>
public sealed class ReplyDraftingOptionsTests
{
    /// <summary>Off is the default and a supported deployment: the composer is the one somebody writes in themselves.</summary>
    [Fact]
    public void Validate_AChatEndpointWithNoReplyDraftingBlock_IsAcceptedAndLeavesTheDraftingOff()
    {
        // Arrange
        var settings = Declared();

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.False(settings.ReplyDrafting.Enabled);
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

    /// <summary>The drafting runs against the declared endpoint and has nowhere to send a conversation without one.</summary>
    [Fact]
    public void Validate_AnEnabledDraftingWithoutAChatEndpoint_IsRefused()
    {
        // Arrange
        var settings = new ChatModelOptions();
        settings.ReplyDrafting.Enabled = true;

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.Contains("no Alias", StringComparison.Ordinal));
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

    private static ChatModelOptions Declared() => new()
    {
        Alias = "answering",
        Model = "a-chat-model",
        ApiKey = new ConfiguredSecret { SecretReference = "env:CHAT_KEY" },
    };

    private static IReadOnlyList<string> Validate(ChatModelOptions settings) =>
    [
        .. settings
            .Validate(new ValidationContext(settings))
            .Select(result => result.ErrorMessage ?? string.Empty),
    ];
}
