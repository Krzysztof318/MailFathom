// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers how the command settles which deployment it is about to send a credential to.</summary>
/// <remarks>
/// This decides where a bearer credential is sent, so guessing is not an option anywhere in it. A value that does not
/// name an address unambiguously is read as a profile name rather than repaired into an address.
/// </remarks>
public sealed class CliOptionsTests
{
    [Fact]
    public void RequestedDeployment_AValueOnTheCommandLine_IsWhatTheCommandActsOn()
    {
        // Act, Assert: what an operator typed beats what their shell was told.
        Assert.Equal("production", CliOptions.RequestedDeployment("production", "staging"));
    }

    [Fact]
    public void RequestedDeployment_SurroundingWhitespace_IsNotPartOfTheName()
    {
        // Act, Assert: a value pasted from a terminal or a script arrives with whatever was around it.
        Assert.Equal("production", CliOptions.RequestedDeployment("  production  ", endpointVariable: null));
    }

    /// <summary>The variable is the shell stating it once for a session, and it beats the stored default.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void RequestedDeployment_NothingOnTheCommandLine_IsWhatTheShellWasTold(string? configuredEndpoint)
    {
        // Act, Assert
        Assert.Equal("staging", CliOptions.RequestedDeployment(configuredEndpoint, "  staging  "));
    }

    /// <summary>Nothing named means the profile the operator last switched to, which the store settles rather than this.</summary>
    [Fact]
    public void RequestedDeployment_NothingAnywhere_NamesNoDeployment()
    {
        // Act, Assert
        Assert.Null(CliOptions.RequestedDeployment(configuredEndpoint: null, endpointVariable: null));
    }

    [Theory]
    [InlineData("https://mail.example.test:8443")]
    [InlineData("http://localhost:8090")]
    public void TryReadAddress_AnAbsoluteHttpAddress_IsReadAsOne(string candidate)
    {
        // Act, Assert: plain HTTP is accepted, because refusing it would leave a loopback deployment and a reverse
        // proxy unreachable, and the endpoint warns about clear text on its own side where it knows what is in front
        // of it.
        Assert.True(CliOptions.TryReadAddress(candidate, out _));
    }

    /// <summary>
    /// A bare host name is not completed with a scheme. Choosing one would decide between a protected and an
    /// unprotected transport for a request that carries a credential, which is not a default to pick for someone — so
    /// it is read as a profile name, and the store says whether one exists.
    /// </summary>
    [Theory]
    [InlineData("mail.example.test:8443")]
    [InlineData("production")]
    [InlineData("/api")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryReadAddress_SomethingThatIsNotAnHttpAddress_IsNotReadAsOne(string? candidate)
    {
        // Act, Assert
        Assert.False(CliOptions.TryReadAddress(candidate, out _));
    }

    /// <summary>A scheme this command cannot speak is not an address it should try, whatever the URI parser makes of it.</summary>
    [Theory]
    [InlineData("ftp://mail.example.test")]
    [InlineData("file:///etc/passwd")]
    public void TryReadAddress_AnAbsoluteUriInAnotherScheme_IsNotReadAsAnAddress(string candidate)
    {
        // Act, Assert
        Assert.False(CliOptions.TryReadAddress(candidate, out _));
    }

    /// <summary>
    /// A deployment may serve several mailboxes, and every command taking this either walks one of them or reads what
    /// was done to one. A default would be a guess at whose mail the operator meant, so there is none and the option is
    /// required wherever it appears.
    /// </summary>
    [Fact]
    public void MailAccount_Always_IsRequiredAndDefaultsToNothing()
    {
        // Act
        var option = CliOptions.MailAccount();

        // Assert
        Assert.True(option.Required);
        Assert.False(option.HasDefaultValue);
    }

    /// <summary>
    /// The narrowing folder is the mirror of the required one, and the difference is the whole point: a command taking
    /// it acts on an account, so omitting it means every folder the account holds mail in rather than a folder nobody
    /// named. Required here would leave the whole-account shape unreachable from the command line.
    /// </summary>
    [Fact]
    public void NarrowedMailFolder_Always_IsOptionalAndDefaultsToNothing()
    {
        // Act
        var option = CliOptions.NarrowedMailFolder();

        // Assert
        Assert.False(option.Required);
        Assert.False(option.HasDefaultValue);
    }

    /// <summary>
    /// An absent size is the deployment's own default rather than a number this tool keeps in step with it, so the
    /// option carries none of its own — and the listing that omits it asks for whatever the endpoint serves.
    /// </summary>
    [Fact]
    public void PageSize_Always_IsOptionalAndDefaultsToNothing()
    {
        // Act
        var option = CliOptions.PageSize("contacts");

        // Assert
        Assert.False(option.Required);
        Assert.False(option.HasDefaultValue);
    }

    /// <summary>The noun is the whole of what differs between the listings, which is what makes one factory serve all five.</summary>
    [Fact]
    public void PageSize_ANounOfTheListing_IsWhatTheDescriptionCounts()
    {
        // Act
        var option = CliOptions.PageSize("classifications");

        // Assert
        Assert.Equal("How many classifications to read. Defaults to what the deployment serves.", option.Description);
    }

    /// <summary>A cursor is handed back unread, so nothing is assumed about a walk nobody started.</summary>
    [Fact]
    public void Cursor_Always_IsOptionalAndDefaultsToNothing()
    {
        // Act
        var option = CliOptions.Cursor();

        // Assert
        Assert.False(option.Required);
        Assert.False(option.HasDefaultValue);
    }

    /// <summary>
    /// The prompt is the default and the flag is the exception, so nothing about it is required. The short form is
    /// part of the contract rather than a convenience: it is what the four commands were each declaring by hand, and
    /// one copy dropping it would leave <c>-y</c> working on three of them.
    /// </summary>
    [Fact]
    public void Confirmed_Always_IsOptionalAndAcceptsTheShortForm()
    {
        // Act
        var option = CliOptions.Confirmed("erasure");

        // Assert
        Assert.False(option.Required);
        Assert.Contains("-y", option.Aliases);
        Assert.Equal("Agree to the erasure without being asked, which is what a scripted run needs.", option.Description);
    }
}
