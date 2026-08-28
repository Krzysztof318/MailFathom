// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Secrets;
using MailFathom.Infrastructure.Secrets.Resolution;
using Npgsql;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Secrets;

public sealed class DatabaseSecretReferenceResolverTests
{
    [Fact]
    public void ClassifyProviderFailure_AProviderTimeout_ReportsRetrievalTimedOut()
    {
        // Arrange
        var exception = new NpgsqlException("The operation timed out.", new TimeoutException());

        // Act
        var failure = DatabaseSecretReferenceResolver.ClassifyProviderFailure(exception);

        // Assert
        Assert.Equal(SecretResolutionFailure.RetrievalTimedOut, failure);
    }

    [Fact]
    public void ClassifyProviderFailure_AProviderTransportFailure_ReportsProviderUnavailable()
    {
        // Arrange
        var exception = new NpgsqlException("The connection failed.", new IOException());

        // Act
        var failure = DatabaseSecretReferenceResolver.ClassifyProviderFailure(exception);

        // Assert
        Assert.Equal(SecretResolutionFailure.ProviderUnavailable, failure);
    }
}
