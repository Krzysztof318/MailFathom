// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Infrastructure.Persistence.Users;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>The scopes a service reading user records resolves its reader and binder from, answering with the two a test states.</summary>
internal static class UserRecordScopes
{
    /// <summary>Opens scopes that resolve the given reader and binder, and nothing else.</summary>
    /// <param name="documents">The reader every scope resolves.</param>
    /// <param name="binder">The binder every scope resolves.</param>
    /// <returns>A scope factory over the two.</returns>
    internal static IServiceScopeFactory Resolving(IUserSettingsDocumentReader documents, UserAccountDocumentBinder binder)
    {
        var provider = Substitute.For<IServiceProvider>();
        var scope = Substitute.For<IServiceScope>();
        var scopes = Substitute.For<IServiceScopeFactory>();

        provider.GetService(typeof(IUserSettingsDocumentReader)).Returns(documents);
        provider.GetService(typeof(UserAccountDocumentBinder)).Returns(binder);
        scope.ServiceProvider.Returns(provider);
        scopes.CreateScope().Returns(scope);

        return scopes;
    }
}
