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
    /// <summary>Builds the authorization of an admitted caller granted exactly the permissions named, acting for the deployment's owner.</summary>
    /// <param name="grantedPermissions">What the entry that admitted the caller resolved to, which is empty for a caller granted nothing.</param>
    /// <returns>The authorization a use case reached by that caller consults.</returns>
    /// <remarks>
    /// An ordinary caller acts for somebody, so this states an owner rather than leaving the principal without one: a
    /// helper that omitted it would arrange the deployment administrator in every test about reading a mailbox, and
    /// those tests would then be proving a refusal instead of what they were written for. A test about a principal
    /// acting for nobody arranges <see cref="ForAdministratorGranted" /> by name.
    /// </remarks>
    internal static AccessAuthorization ForCallerGranted(params MailFathomPermission[] grantedPermissions) =>
        ForOwnerGranted(SyntheticMailOwner.Deployment, grantedPermissions);

    /// <summary>Builds the authorization of an admitted caller acting for one named owner.</summary>
    /// <param name="owner">The owner whose mail the caller was admitted to act on.</param>
    /// <param name="grantedPermissions">What the entry that admitted the caller resolved to.</param>
    /// <returns>The authorization a use case reached by that caller consults.</returns>
    internal static AccessAuthorization ForOwnerGranted(
        MailOwnerId owner,
        params MailFathomPermission[] grantedPermissions) =>
        ForPrincipal(AuthorizedPrincipal.CallerActingFor(owner, "test-caller", grantedPermissions));

    /// <summary>Builds the authorization of the deployment administrator, which is a caller acting for no owner.</summary>
    /// <param name="grantedPermissions">What the entry that admitted the administrator resolved to.</param>
    /// <returns>The authorization a use case reached by that caller consults.</returns>
    internal static AccessAuthorization ForAdministratorGranted(params MailFathomPermission[] grantedPermissions) =>
        ForPrincipal(AuthorizedPrincipal.Caller("test-administrator", grantedPermissions));

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
