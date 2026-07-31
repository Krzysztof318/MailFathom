// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Text.Json;
using MailMcp.Domain.Transport;
using Xunit;

namespace MailMcp.Domain.UnitTests;

public sealed class MailAuthenticationMechanismJsonConverterTests
{
    [Fact]
    public void Serialize_SupportedMechanism_WritesTheRegisteredSaslNameWithoutExplicitConverterRegistration()
    {
        // Arrange, Act
        var json = JsonSerializer.Serialize(MailAuthenticationMechanism.ScramSha256);

        // Assert
        Assert.Equal("\"SCRAM-SHA-256\"", json);
    }

    [Fact]
    public void Deserialize_MixedCaseSaslName_ParsesTheMechanism()
    {
        // Arrange, Act
        var mechanism = JsonSerializer.Deserialize<MailAuthenticationMechanism>("\"scram-sha-256\"");

        // Assert
        Assert.Equal(MailAuthenticationMechanism.ScramSha256, mechanism);
    }

    [Fact]
    public void Deserialize_MechanismList_RoundTripsThroughTheSaslNames()
    {
        // Arrange
        MailAuthenticationMechanism[] mechanisms = [MailAuthenticationMechanism.ScramSha512Plus, MailAuthenticationMechanism.Plain];

        // Act
        var json = JsonSerializer.Serialize(mechanisms);
        var restored = JsonSerializer.Deserialize<MailAuthenticationMechanism[]>(json);

        // Assert
        Assert.Equal("[\"SCRAM-SHA-512-PLUS\",\"PLAIN\"]", json);
        Assert.Equal(mechanisms, restored);
    }

    [Fact]
    public void Serialize_MechanismDictionaryKey_WritesTheSaslNameAsThePropertyName()
    {
        // Arrange
        var permittedByMechanism = new Dictionary<MailAuthenticationMechanism, bool>
        {
            [MailAuthenticationMechanism.CramMd5] = true,
        };

        // Act
        var json = JsonSerializer.Serialize(permittedByMechanism);
        var restored = JsonSerializer.Deserialize<Dictionary<MailAuthenticationMechanism, bool>>(json);

        // Assert
        Assert.Equal("{\"CRAM-MD5\":true}", json);
        Assert.Equal(permittedByMechanism, restored);
    }

    [Theory]
    [InlineData("\"GSSAPI\"")]
    [InlineData("\"\"")]
    [InlineData("7")]
    public void Deserialize_UnsupportedPayload_ThrowsJsonException(string json)
    {
        // Arrange, Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MailAuthenticationMechanism>(json));
    }

    [Fact]
    public void Serialize_StructDefault_ThrowsInsteadOfWritingAnUnusableMechanism()
    {
        // Arrange
        MailAuthenticationMechanism unspecifiedMechanism = default;

        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(unspecifiedMechanism));
    }
}
