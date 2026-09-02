// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets.Database;
using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Secrets.Database;

public sealed class DatabaseSecretBootstrapResolverTests
{
    [Fact]
    public async Task ResolveAsync_ADatabaseReference_ReportsTheBootstrapCycleWithoutReadingAnything()
    {
        // Arrange
        var resolver = new DatabaseSecretBootstrapResolver();
        Assert.True(SecretReference.TryParse(
            "database:019925df-96f4-7c6d-8f91-b9f6cf27f5b2",
            out var reference,
            out _));

        // Act
        var result = await resolver.ResolveAsync(reference, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(SecretResolutionFailure.BootstrapDependencyNotPermitted, result.Failure);
    }
}
