// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Deployment;

namespace MailFathom.Client.UnitTests.Deployment;

/// <summary>How written text becomes an address, which has to be the same reading wherever it was written.</summary>
/// <remarks>The HTTPS default is the claim with a consequence: somebody typing their server's name into a screen and somebody writing it into a configuration file get the same address, and neither of them gets clear text by omitting a scheme.</remarks>
public sealed class DeploymentAddressTextTests
{
    [Theory]
    [InlineData("mail.example.test", "https://mail.example.test/")]
    [InlineData("//mail.example.test", "https://mail.example.test/")]
    [InlineData("  mail.example.test  ", "https://mail.example.test/")]
    [InlineData("mail.example.test:8443", "https://mail.example.test:8443/")]
    public void TryRead_TextWithoutAScheme_IsReadAsHttps(string written, string expected)
    {
        // Act
        var read = DeploymentAddressText.TryRead(written, out var address);

        // Assert
        Assert.True(read);
        Assert.Equal(new Uri(expected), address);
    }

    [Theory]
    [InlineData("https://mail.example.test/")]
    [InlineData("http://127.0.0.1:8080/")]
    public void TryRead_TextStatingItsScheme_KeepsIt(string written)
    {
        // Act
        var read = DeploymentAddressText.TryRead(written, out var address);

        // Assert
        Assert.True(read);
        Assert.Equal(new Uri(written), address);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryRead_NothingWritten_NamesNoAddress(string? written)
    {
        // Act
        var read = DeploymentAddressText.TryRead(written, out var address);

        // Assert
        Assert.False(read);
        Assert.Null(address);
    }

    [Fact]
    public void TryRead_TextThatIsNotAnAddress_NamesNoAddress()
    {
        // Act
        var read = DeploymentAddressText.TryRead("ht!tp://mail.example.test", out var address);

        // Assert
        Assert.False(read);
        Assert.Null(address);
    }
}
