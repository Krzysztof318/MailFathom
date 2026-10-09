// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Users;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>The scopes a service reading mail accounts resolves its store from, answering with the one a test states.</summary>
internal static class MailAccountRecordScopes
{
    /// <summary>Opens scopes that resolve the given store and nothing else.</summary>
    /// <param name="accounts">The store every scope resolves.</param>
    /// <returns>A scope factory over it.</returns>
    internal static IServiceScopeFactory Resolving(IMailAccountRecordStore accounts)
    {
        var provider = Substitute.For<IServiceProvider>();
        var scope = Substitute.For<IServiceScope>();
        var scopes = Substitute.For<IServiceScopeFactory>();

        provider.GetService(typeof(IMailAccountRecordStore)).Returns(accounts);
        scope.ServiceProvider.Returns(provider);
        scopes.CreateScope().Returns(scope);

        return scopes;
    }
}
