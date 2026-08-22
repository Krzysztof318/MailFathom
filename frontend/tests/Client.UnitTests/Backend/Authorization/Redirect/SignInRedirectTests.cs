// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Authorization.Redirect;

namespace MailFathom.Client.UnitTests.Backend.Authorization.Redirect;

/// <summary>That a redirect is read out of attacker-influenced text without any of it being assumed well formed.</summary>
public sealed class SignInRedirectTests
{
    [Fact]
    public void FromQuery_AnApproval_ReadsTheCodeAndTheStateItMustBeMatchedAgainst()
    {
        // Arrange, Act
        var redirect = SignInRedirect.FromQuery("?code=the-code&state=ABCD1234");

        // Assert
        Assert.Equal("the-code", redirect.Code);
        Assert.Equal("ABCD1234", redirect.State);
        Assert.Null(redirect.Error);
    }

    [Fact]
    public void FromQuery_NoLeadingQuestionMark_ReadsTheSameThing()
    {
        // Arrange, Act
        var redirect = SignInRedirect.FromQuery("code=the-code&state=ABCD1234");

        // Assert
        Assert.Equal("the-code", redirect.Code);
        Assert.Equal("ABCD1234", redirect.State);
    }

    [Fact]
    public void FromQuery_ARefusal_ReadsTheErrorRatherThanACode()
    {
        // Arrange, Act
        var redirect = SignInRedirect.FromQuery("?error=access_denied&state=ABCD1234");

        // Assert
        Assert.Null(redirect.Code);
        Assert.Equal("access_denied", redirect.Error);
    }

    [Fact]
    public void FromQuery_APercentEncodedValue_IsDecodedTheWayItWasWritten()
    {
        // Arrange, Act
        var redirect = SignInRedirect.FromQuery("?code=a%2Fb%2Bc%3Dd&state=x+y");

        // Assert
        Assert.Equal("a/b+c=d", redirect.Code);
        Assert.Equal("x y", redirect.State);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("?")]
    [InlineData("?code")]
    [InlineData("?=orphan")]
    public void FromQuery_AQueryCarryingNothingUsable_ReadsEveryPartAsAbsentRatherThanThrowing(string? query)
    {
        // Arrange, Act
        var redirect = SignInRedirect.FromQuery(query);

        // Assert
        Assert.Null(redirect.Code);
        Assert.Null(redirect.State);
        Assert.Null(redirect.Error);
    }

    [Fact]
    public void FromQuery_AParameterTheFlowDoesNotKnow_IsIgnoredRatherThanRefused()
    {
        // Arrange, Act
        var redirect = SignInRedirect.FromQuery("?session_state=abc&code=the-code&state=ABCD1234&iss=https%3A%2F%2Fissuer.example");

        // Assert
        Assert.Equal("the-code", redirect.Code);
        Assert.Equal("ABCD1234", redirect.State);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("?favicon=1")]
    public void CarriesAnAnswer_ARequestThatWasNeverPartOfThisFlow_SaysSo(string? query)
    {
        // Arrange, Act
        var redirect = SignInRedirect.FromQuery(query);

        // Assert
        Assert.False(redirect.CarriesAnAnswer);
    }

    [Theory]
    [InlineData("?code=the-code&state=ABCD1234")]
    [InlineData("?error=access_denied&state=ABCD1234")]
    [InlineData("?state=ABCD1234")]
    public void CarriesAnAnswer_ARedirectAddressedToThisFlow_SaysSoWhicheverWayItAnswered(string query)
    {
        // Arrange, Act
        var redirect = SignInRedirect.FromQuery(query);

        // Assert
        Assert.True(redirect.CarriesAnAnswer);
    }
}
