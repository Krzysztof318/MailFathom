// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Infrastructure.Persistence.Sessions;
using NSubstitute;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Sessions;

public sealed class EfCorePersistenceSessionAccessorTests
{
    [Fact]
    public async Task JoinAsync_SessionBackedByAnotherPersistenceProvider_Throws()
    {
        // Arrange
        var foreignSession = Substitute.For<IPersistenceSession>();

        // Act
        var thrown = await Assert.ThrowsAsync<ArgumentException>(
            async () => await EfCorePersistenceSessionAccessor.JoinAsync(foreignSession, CancellationToken.None));

        // Assert
        Assert.Equal("session", thrown.ParamName);
    }

    [Fact]
    public async Task JoinAsync_NullSession_ThrowsArgumentNullException()
    {
        // Arrange
        IPersistenceSession? session = null;

        // Act
        var thrown = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await EfCorePersistenceSessionAccessor.JoinAsync(session!, CancellationToken.None));

        // Assert
        Assert.Equal("session", thrown.ParamName);
    }
}
