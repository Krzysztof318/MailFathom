// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Host.Configuration.Chat;
using MailFathom.Host.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Chat;

/// <summary>Covers what a conversation-state declaration has to say before an instance will start on it.</summary>
/// <remarks>
/// Reached through the chat declaration that owns it rather than in isolation, because that is where an operator writes
/// it and because the one rule about it is that a derivation needs an endpoint to send a conversation to.
/// </remarks>
public sealed class ThreadStateOptionsTests
{
    /// <summary>Off is the default and a supported deployment: a conversation reads as it read before this existed.</summary>
    [Fact]
    public void Validate_AChatEndpointWithNoThreadStateBlock_IsAcceptedAndLeavesTheDerivationOff()
    {
        // Arrange
        var settings = Declared();

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.False(settings.ThreadState.Enabled);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_AnEnabledDerivationOnADeclaredEndpoint_IsAccepted()
    {
        // Arrange
        var settings = Declared();
        settings.ThreadState.Enabled = true;

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>The derivation runs against the declared endpoint and has nowhere to send a conversation without one.</summary>
    [Fact]
    public void Validate_AnEnabledDerivationWithoutAChatEndpoint_IsRefused()
    {
        // Arrange
        var settings = new ChatModelOptions();
        settings.ThreadState.Enabled = true;

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.Contains("no model under Chat:Models", StringComparison.Ordinal));
    }

    private static ChatModelOptions Declared() => DeclaredChatModels.Section();

    private static IReadOnlyList<string> Validate(ChatModelOptions settings) =>
    [
        .. settings
            .Validate(new ValidationContext(settings))
            .Select(result => result.ErrorMessage ?? string.Empty),
    ];
}
