// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Administration;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers how one request's filters reach the administrative endpoint.</summary>
/// <remarks>
/// Every narrowed reading the command performs composes its query string here, so an escaping decision taken wrongly is
/// taken wrongly for all of them at once. That is the trade this type was written for, and it is why the escaping is
/// asserted against a value an operator can actually write rather than against a bare word.
/// </remarks>
public sealed class AdminQueryStringTests
{
    /// <summary>A request narrowing nothing asks for the route itself, which is what leaves a listing at the deployment's own defaults.</summary>
    [Fact]
    public void ToString_NoFilterNamed_IsAnEmptyString()
    {
        // Act, Assert
        Assert.Equal(string.Empty, new AdminQueryString().ToString());
    }

    [Fact]
    public void ToString_SeveralFilters_OpensWithAQuestionMarkAndSeparatesWithAmpersands()
    {
        // Act
        var query = new AdminQueryString()
            .Add("account", "personal")
            .Add("pageSize", 25)
            .ToString();

        // Assert
        Assert.Equal("?account=personal&pageSize=25", query);
    }

    /// <summary>
    /// A rule name may carry a space and a cursor is base64url, so a value written into the query unescaped is a
    /// request that means something other than what the operator asked for.
    /// </summary>
    [Theory]
    [InlineData("Move newsletters", "rule=Move%20newsletters")]
    [InlineData("a&b=c", "rule=a%26b%3Dc")]
    [InlineData("zażółć", "rule=za%C5%BC%C3%B3%C5%82%C4%87")]
    public void Add_AValueAnOperatorWrote_IsEscapedForTheQueryString(string value, string expected)
    {
        // Act
        var query = new AdminQueryString().Add("rule", value).ToString();

        // Assert
        Assert.Equal($"?{expected}", query);
    }

    /// <summary>
    /// A filter nobody named is left out rather than sent empty, so the deployment reads it as every account it serves
    /// rather than as one more shape to have an opinion about.
    /// </summary>
    [Fact]
    public void Add_ValuesTheOperatorLeftOut_AreNotInTheQueryString()
    {
        // Act
        var query = new AdminQueryString()
            .Add("account", (string?)null)
            .Add("stage", string.Empty)
            .Add("pageSize", (int?)null)
            .Add("email", (Guid?)null)
            .Add("cursor", "b3V0Ym94LTE")
            .ToString();

        // Assert
        Assert.Equal("?cursor=b3V0Ym94LTE", query);
    }

    /// <summary>An identifier reaches every administrative route in the hyphenated form, whatever a machine's own formatting would be.</summary>
    [Fact]
    public void Add_AnIdentifier_IsWrittenHyphenatedAndInvariantly()
    {
        // Arrange
        var email = new Guid("8a1d4f2e-6c3b-4a91-9f77-2b5e0d1c4a63");

        // Act
        var query = new AdminQueryString().Add("email", email).ToString();

        // Assert
        Assert.Equal("?email=8a1d4f2e-6c3b-4a91-9f77-2b5e0d1c4a63", query);
    }

    /// <summary>A count is written invariantly, so a machine whose locale groups digits does not send a size the endpoint cannot read.</summary>
    [Fact]
    public void Add_ACount_IsWrittenInvariantly()
    {
        // Act
        var query = new AdminQueryString().Add("pageSize", 22_500).ToString();

        // Assert
        Assert.Equal("?pageSize=22500", query);
    }
}
