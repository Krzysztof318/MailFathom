// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Application.Persistence;
using MailMcp.Infrastructure.Persistence;
using NSubstitute;
using Xunit;

namespace MailMcp.Infrastructure.UnitTests;

public sealed class EfCorePersistenceSessionAccessorTests
{
    [Fact]
    public void DbContextOf_SessionBackedByAnotherPersistenceProvider_Throws()
    {
        // Arrange
        var foreignSession = Substitute.For<IPersistenceSession>();

        // Act
        var thrown = Assert.Throws<ArgumentException>(() => EfCorePersistenceSessionAccessor.DbContextOf(foreignSession));

        // Assert
        Assert.Equal("session", thrown.ParamName);
    }

    [Fact]
    public void DbContextOf_NullSession_ThrowsArgumentNullException()
    {
        // Arrange
        IPersistenceSession? session = null;

        // Act
        var thrown = Assert.Throws<ArgumentNullException>(() => EfCorePersistenceSessionAccessor.DbContextOf(session!));

        // Assert
        Assert.Equal("session", thrown.ParamName);
    }
}
