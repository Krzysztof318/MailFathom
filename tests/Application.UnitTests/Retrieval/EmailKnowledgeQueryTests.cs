// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using MailFathom.Application.Emails.SearchEmails;
using MailFathom.Application.Retrieval;
using Xunit;

namespace MailFathom.Application.UnitTests.Retrieval;

/// <summary>Covers the parity the knowledge query claims with the search request a caller reaches the same mail through.</summary>
public sealed class EmailKnowledgeQueryTests
{
    /// <summary>
    /// The members of <see cref="SearchEmailsRequest" /> a knowledge query withholds, each for the reason
    /// <see cref="EmailKnowledgeQuery" />'s own remarks give beside it.
    /// </summary>
    /// <remarks>
    /// Named rather than derived, because a filter that goes missing and a filter that is withheld on purpose look
    /// identical from the outside: only a written decision tells them apart, and this is where that decision is read
    /// back. A member added to the request reaches the query or reaches this list, and nothing else passes.
    /// </remarks>
    private static readonly string[] WithheldFromAKnowledgeQuery =
    [
        nameof(SearchEmailsRequest.Accounts),
        nameof(SearchEmailsRequest.Folders),
        nameof(SearchEmailsRequest.IncludeJunkMail),
        nameof(SearchEmailsRequest.ResultLimit),
    ];

    /// <summary>A filter one surface can express and the other cannot is a disagreement neither of them announces.</summary>
    [Fact]
    public void EmailKnowledgeQuery_AgainstTheSearchRequest_DeclaresEveryFilterItDoesNotWithhold()
    {
        // Arrange
        var expected = PublishedPropertyNames(typeof(SearchEmailsRequest))
            .Except(WithheldFromAKnowledgeQuery)
            .Order(StringComparer.Ordinal);

        // Act
        var declared = PublishedPropertyNames(typeof(EmailKnowledgeQuery)).Order(StringComparer.Ordinal);

        // Assert
        Assert.Equal(expected, declared);
    }

    private static IEnumerable<string> PublishedPropertyNames(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name);
}
