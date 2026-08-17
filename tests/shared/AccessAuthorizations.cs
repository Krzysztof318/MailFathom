// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.TestSupport;

/// <summary>Builds the authorization a use case asks, over a principal a test states.</summary>
/// <remarks>
/// Every use case behind a protected surface now takes one, so a test that composes one by hand would be writing the
/// same three lines wherever a caller is arranged. What a test states here is the principal alone, because that is the
/// whole of what the application layer learns about the outside of a request; a substitute would leave the same fact
/// spelled differently in each suite.
/// </remarks>
internal static class AccessAuthorizations
{
    /// <summary>Builds the authorization of an admitted caller granted exactly the permissions named.</summary>
    /// <param name="grantedPermissions">What the entry that admitted the caller resolved to, which is empty for a caller granted nothing.</param>
    /// <returns>The authorization a use case reached by that caller consults.</returns>
    internal static AccessAuthorization ForCallerGranted(params MailFathomPermission[] grantedPermissions) =>
        ForPrincipal(AuthorizedPrincipal.Caller("test-caller", grantedPermissions));

    /// <summary>Builds the authorization of work reached under a stated principal, or under none.</summary>
    /// <param name="principal">Whoever the work is running for, or <see langword="null" /> for an entrypoint that stated nothing.</param>
    /// <returns>The authorization a use case reached that way consults.</returns>
    internal static AccessAuthorization ForPrincipal(AuthorizedPrincipal? principal) =>
        new(new StatedPrincipalSource(principal));

    /// <summary>Reports the one principal a test stated, for the whole of that test's unit of work.</summary>
    private sealed class StatedPrincipalSource(AuthorizedPrincipal? principal) : IAuthorizedPrincipalSource
    {
        public AuthorizedPrincipal? Current => principal;
    }
}
