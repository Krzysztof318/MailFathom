// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Presentation;

/// <summary>Covers what a citation resolves to, and the identities it publishes to get there.</summary>
public sealed class PresentationCitationTargetTests
{
    private static readonly StoredEmailId Email =
        StoredEmailId.Create(new Guid("11111111-1111-1111-1111-111111111111"));

    private static JsonSerializerOptions Contract => PresentationPlanJsonContext.Default.Options;

    /// <summary>Every target names the email, because that is what a reader is taken to whichever of the three it is.</summary>
    [Fact]
    public void Email_EveryTarget_NamesTheEmailTheCitationIsFollowedTo()
    {
        // Arrange
        PresentationCitationTarget[] targets =
        [
            new EmailCitationTarget(Email),
            new FragmentCitationTarget(Email, EmailChunkId.Create(new Guid("33333333-3333-3333-3333-333333333333"))),
            new AttachmentCitationTarget(Email, attachmentPosition: 2),
        ];

        // Act, Assert
        Assert.All(targets, target => Assert.Equal(Email, target.Email));
    }

    /// <summary>A position is where the attachment sits in the message, so there is no such thing as a negative one.</summary>
    [Fact]
    public void Constructor_ANegativeAttachmentPosition_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new AttachmentCitationTarget(Email, attachmentPosition: -1));
    }

    [Fact]
    public void Serialization_AnAttachmentTarget_WritesTheMessageAndThePositionItIsAddressedBy()
    {
        // Arrange
        PresentationCitationTarget target = new AttachmentCitationTarget(Email, attachmentPosition: 2);

        // Act
        var json = JsonSerializer.Serialize(target, Contract);
        var read = JsonSerializer.Deserialize<PresentationCitationTarget>(json, Contract);

        // Assert
        Assert.Contains($"\"kind\":\"{AttachmentCitationTarget.Kind}\"", json, StringComparison.Ordinal);
        Assert.Contains("\"attachmentPosition\":2", json, StringComparison.Ordinal);
        Assert.Equal(target, read);
    }

    /// <summary>An identifier is published as the UUID the client API names one by, so anything else is not one.</summary>
    [Theory]
    [InlineData("{\"kind\":\"email\",\"email\":7}")]
    [InlineData("{\"kind\":\"email\",\"email\":\"not-a-uuid\"}")]
    [InlineData("{\"kind\":\"email\",\"email\":\"00000000-0000-0000-0000-000000000000\"}")]
    public void Deserialization_AnEmailIdentityThatNamesNothing_IsRefused(string json)
    {
        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PresentationCitationTarget>(json, Contract));
    }

    [Theory]
    [InlineData("{\"kind\":\"fragment\",\"email\":\"11111111-1111-1111-1111-111111111111\",\"fragment\":7}")]
    [InlineData("{\"kind\":\"fragment\",\"email\":\"11111111-1111-1111-1111-111111111111\",\"fragment\":\"not-a-uuid\"}")]
    public void Deserialization_AFragmentIdentityThatNamesNothing_IsRefused(string json)
    {
        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PresentationCitationTarget>(json, Contract));
    }

    /// <summary>A target the contract does not declare is a service ahead of this build rather than a shape to guess at.</summary>
    [Fact]
    public void Deserialization_AKindTheContractDoesNotDeclare_IsRefused()
    {
        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PresentationCitationTarget>(
            "{\"kind\":\"search\",\"email\":\"11111111-1111-1111-1111-111111111111\"}",
            Contract));
    }
}
