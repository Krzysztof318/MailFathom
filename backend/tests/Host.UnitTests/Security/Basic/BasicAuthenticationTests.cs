// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Security.Basic;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Basic;

/// <summary>Covers the challenge a surface accepting a password refuses with.</summary>
/// <remarks>
/// A password is the one credential a client does not send until a challenge asks for it, so the header is what makes
/// the method usable at all — and it is also the one thing every refused caller reads, which is why nothing in it may
/// differ between a request that presented nothing and one that presented a wrong password.
/// </remarks>
public sealed class BasicAuthenticationTests
{
    [Fact]
    public void WriteChallenge_ARefusedRequest_AnswersAnEmptyUnauthorized()
    {
        // Arrange: an observable stream rather than the context's default, which is Stream.Null and reports no length
        // whatever is written to it — so a challenge that grew a body would leave the emptiness assertion green.
        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        context.Response.Body = body;

        // Act
        BasicAuthentication.WriteChallenge(context.Response);

        // Assert
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal(0, body.Length);
    }

    /// <summary>Two schemes are offered as two values of one header, which is what RFC 7235 says a server accepting both does.</summary>
    [Fact]
    public void WriteChallenge_ARefusedRequest_OffersTheBearerAndThePasswordSchemesBesideEachOther()
    {
        // Arrange
        var context = new DefaultHttpContext();

        // Act
        BasicAuthentication.WriteChallenge(context.Response);

        // Assert
        Assert.Equal(
            ["Bearer realm=\"MailFathom\"", "Basic realm=\"MailFathom\", charset=\"UTF-8\""],
            context.Response.Headers[HeaderNames.WWWAuthenticate].Select(value => value ?? string.Empty));
    }

    /// <summary>Without the parameter a client is left to guess an encoding, and two clients guessing differently would send two credentials for one typed password.</summary>
    [Fact]
    public void WriteChallenge_ARefusedRequest_NamesTheOneEncodingTheSchemePermits()
    {
        // Arrange
        var context = new DefaultHttpContext();

        // Act
        BasicAuthentication.WriteChallenge(context.Response);

        // Assert
        Assert.Contains(
            "charset=\"UTF-8\"",
            context.Response.Headers[HeaderNames.WWWAuthenticate].ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>Every refusal on the surface answers with this one header, so nothing in it says which half of a credential was wrong or whether a username exists.</summary>
    [Fact]
    public void WriteChallenge_ARefusedRequest_DescribesNothingAboutTheCredential()
    {
        // Arrange
        var context = new DefaultHttpContext();

        // Act
        BasicAuthentication.WriteChallenge(context.Response);

        // Assert
        var challenge = context.Response.Headers[HeaderNames.WWWAuthenticate].ToString();

        Assert.DoesNotContain("error", challenge, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user", challenge, StringComparison.OrdinalIgnoreCase);
    }
}
