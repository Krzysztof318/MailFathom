// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Host.Configuration.Chat;
using MailFathom.Host.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Chat;

/// <summary>Covers what a contact-relationship declaration has to say before an instance will start on it.</summary>
/// <remarks>
/// Reached through the chat declaration that owns it rather than in isolation, because that is where an operator writes
/// it and because the one rule about it is that a card needs an endpoint to send a correspondence to.
/// </remarks>
public sealed class ContactRelationshipOptionsTests
{
    /// <summary>On is the default wherever an endpoint is declared, so a deployment that wrote no block heads an opened contact with a card.</summary>
    [Fact]
    public void Validate_AChatEndpointWithNoContactRelationshipBlock_IsAcceptedAndLeavesTheCardOn()
    {
        // Arrange
        var settings = Declared();

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.True(settings.ContactRelationship.Enabled);
        Assert.Empty(errors);
    }

    /// <summary>Turning it off is a supported deployment, and it is the operator's spend decision rather than a lesser instance.</summary>
    [Fact]
    public void Validate_ADeclinedCardOnADeclaredEndpoint_IsAccepted()
    {
        // Arrange
        var settings = Declared();
        settings.ContactRelationship.Enabled = false;

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>
    /// A card left on declares no provider, because on is what a section nobody wrote already reads: taking it as
    /// intent would refuse every deployment that never opened the block, which is most of them.
    /// </summary>
    [Fact]
    public void Validate_TheCardLeftOnWithNoChatEndpoint_DeclaresNoProvider()
    {
        // Arrange
        var settings = new ChatModelOptions();

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.False(settings.IsConfigured);
        Assert.True(settings.ContactRelationship.Enabled);
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
