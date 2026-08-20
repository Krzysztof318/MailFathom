// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Security.ApiKeys;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.ApiKeys;

/// <summary>Covers the challenge every credential method on a surface refuses with.</summary>
/// <remarks>
/// The header is the one thing a refused client reads, and every method produces the same one so that which credential
/// was wrong is never described. It is asserted here rather than through a handler because the string is the contract:
/// a scheme that composed its own could differ from this by a space and nothing would say so.
/// </remarks>
public sealed class ApiKeyAuthenticationTests
{
    [Fact]
    public void WriteBareChallenge_ARefusedRequest_AnswersAnEmptyUnauthorizedNamingTheSchemeAndRealm()
    {
        // Arrange
        var context = new DefaultHttpContext();

        // Act
        ApiKeyAuthentication.WriteBareChallenge(context.Response);

        // Assert
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal("Bearer realm=\"MailFathom\"", context.Response.Headers[HeaderNames.WWWAuthenticate]);
        Assert.Equal(0, context.Response.Body.Length);
    }

    /// <summary>An error code or a description would begin to say which credential was wrong, so the challenge carries neither.</summary>
    [Fact]
    public void WriteBareChallenge_ARefusedRequest_DescribesNothingAboutTheCredential()
    {
        // Arrange
        var context = new DefaultHttpContext();

        // Act
        ApiKeyAuthentication.WriteBareChallenge(context.Response);

        // Assert
        var challenge = context.Response.Headers[HeaderNames.WWWAuthenticate].ToString();
        Assert.DoesNotContain("error", challenge, StringComparison.OrdinalIgnoreCase);
    }
}
