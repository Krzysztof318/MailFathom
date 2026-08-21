// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts;
using Xunit;

namespace MailFathom.Domain.UnitTests.Contacts;

public sealed class ContactIdTests
{
    /// <summary>The identity every part of the system names a person by is the value it was created from.</summary>
    [Fact]
    public void Create_ANonEmptyUuid_KeepsItAndReadsBackAsThatValue()
    {
        // Arrange
        var value = Guid.CreateVersion7(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));

        // Act
        var contactId = ContactId.Create(value);

        // Assert
        Assert.Equal(value, contactId.Value);
        Assert.Equal(value.ToString(), contactId.ToString());
    }

    /// <summary>An empty identifier names nobody, and it is the one value a caller reaches by forgetting to mint one.</summary>
    [Fact]
    public void Create_TheEmptyUuid_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => ContactId.Create(Guid.Empty));
    }
}
