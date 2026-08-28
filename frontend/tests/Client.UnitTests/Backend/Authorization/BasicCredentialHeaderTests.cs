// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text;
using MailFathom.Client.Backend.Authorization;

namespace MailFathom.Client.UnitTests.Backend.Authorization;

/// <summary>The header a credential travels in, and the challenge that says whether one was invited at all.</summary>
public sealed class BasicCredentialHeaderTests
{
    /// <summary>RFC 7617: one Base64 field, the two halves separated by a colon, encoded as UTF-8.</summary>
    [Fact]
    public void ComposedFrom_ACredential_IsTheEncodedFieldRfc7617Defines()
    {
        // Arrange
        var credential = new OwnerCredential("ada", "a-long-password");

        // Act
        var header = BasicCredentialHeader.ComposedFrom(credential);

        // Assert
        Assert.Equal("Basic", header.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("ada:a-long-password")), header.Parameter);
    }

    /// <summary>
    /// UTF-8 rather than the transport's own default, which is the one encoding the specification's <c>charset</c>
    /// parameter permits and the one MailFathom's own challenge names. Any other would deliver characters nobody typed.
    /// </summary>
    [Fact]
    public void ComposedFrom_APasswordOutsideUsAscii_IsEncodedAsUtf8()
    {
        // Arrange
        var credential = new OwnerCredential("ada", "zażółć-gęślą-jaźń");

        // Act
        var header = BasicCredentialHeader.ComposedFrom(credential);

        // Assert
        Assert.Equal(
            "ada:zażółć-gęślą-jaźń",
            Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter!)));
    }

    /// <summary>A password may carry a colon, and the first one is the split, so every later one has to survive.</summary>
    [Fact]
    public void ComposedFrom_APasswordCarryingAColon_LeavesItInThePasswordHalf()
    {
        // Arrange
        var credential = new OwnerCredential("ada", "a:long:password");

        // Act
        var header = BasicCredentialHeader.ComposedFrom(credential);

        // Assert
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter!));

        Assert.Equal("ada:a:long:password", decoded);
        Assert.Equal("a:long:password", decoded[(decoded.IndexOf(':', StringComparison.Ordinal) + 1)..]);
    }

    /// <summary>A deployment with the password method configured names the scheme beside whatever else it offers.</summary>
    [Fact]
    public void InvitesAPassword_ARefusalNamingTheScheme_IsAnInvitation()
    {
        // Arrange
        using var refusal = new HttpResponseMessage(HttpStatusCode.Unauthorized);

        refusal.Headers.TryAddWithoutValidation("WWW-Authenticate", "Bearer");
        refusal.Headers.TryAddWithoutValidation("WWW-Authenticate", "Basic realm=\"MailFathom\", charset=\"UTF-8\"");

        // Act, Assert
        Assert.True(BasicCredentialHeader.InvitesAPassword(refusal));
    }

    /// <summary>A scheme name is case-insensitive in RFC 7235, so a deployment writing it differently still offers it.</summary>
    [Fact]
    public void InvitesAPassword_ARefusalNamingTheSchemeInAnotherCase_IsStillAnInvitation()
    {
        // Arrange
        using var refusal = new HttpResponseMessage(HttpStatusCode.Unauthorized);

        refusal.Headers.TryAddWithoutValidation("WWW-Authenticate", "basic realm=\"MailFathom\"");

        // Act, Assert
        Assert.True(BasicCredentialHeader.InvitesAPassword(refusal));
    }

    /// <summary>The distinction the sign-in screen turns into two different sentences.</summary>
    [Fact]
    public void InvitesAPassword_ARefusalOfferingSomeOtherScheme_IsNotAnInvitation()
    {
        // Arrange
        using var refusal = new HttpResponseMessage(HttpStatusCode.Unauthorized);

        refusal.Headers.TryAddWithoutValidation("WWW-Authenticate", "Bearer");

        // Act, Assert
        Assert.False(BasicCredentialHeader.InvitesAPassword(refusal));
    }

    /// <summary>A refusal that challenged nothing at all offers no password either.</summary>
    [Fact]
    public void InvitesAPassword_ARefusalCarryingNoChallenge_IsNotAnInvitation()
    {
        // Arrange
        using var refusal = new HttpResponseMessage(HttpStatusCode.Forbidden);

        // Act, Assert
        Assert.False(BasicCredentialHeader.InvitesAPassword(refusal));
    }
}
