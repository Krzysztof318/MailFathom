// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.AI.UnitTests.Chat;

/// <summary>Covers when a declared endpoint sends the session a request belongs to.</summary>
/// <remarks>OpenRouter is the only provider known to route by a session, so the declaration is honoured there and nowhere else.</remarks>
public sealed class ChatEndpointTests
{
    [Theory]
    [InlineData("https://openrouter.ai/api/v1/")]
    [InlineData("https://OpenRouter.AI/api/v1")]
    [InlineData("https://eu.openrouter.ai/api/v1/")]
    public void SendsStickySessions_DeclaredOnAnOpenRouterAddress_IsTrue(string address)
    {
        // Arrange
        var endpoint = ChatDeclarations.Endpoint(address: address, stickySessions: true);

        // Act, Assert
        Assert.True(endpoint.SendsStickySessions);
    }

    /// <summary>A host that merely ends in the same letters, or contains them, is somebody else's server.</summary>
    [Theory]
    [InlineData("https://provider.invalid/v1/")]
    [InlineData("https://notopenrouter.ai/api/v1/")]
    [InlineData("https://openrouter.ai.example.invalid/api/v1/")]
    [InlineData(null)]
    public void SendsStickySessions_DeclaredAnywhereElse_IsFalse(string? address)
    {
        // Arrange
        var endpoint = ChatDeclarations.Endpoint(address: address, stickySessions: true);

        // Act, Assert
        Assert.False(endpoint.SendsStickySessions);
    }

    [Fact]
    public void SendsStickySessions_AnOpenRouterAddressNotDeclaringThem_IsFalse()
    {
        // Arrange
        var endpoint = ChatDeclarations.Endpoint(address: "https://openrouter.ai/api/v1/");

        // Act, Assert
        Assert.False(endpoint.SendsStickySessions);
    }
}
