// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.Mcp.UnitTests.TestDoubles;

/// <summary>Stands in for whatever admitted the work, so a test can say who is asking and what they hold.</summary>
/// <remarks>
/// Hand-written rather than substituted because <see langword="null" /> is one of the answers under test — a use case
/// reached under no principal at all — and a substitute returning it reads as a stub nobody configured rather than as
/// the case the test is about.
/// </remarks>
internal sealed class StubAuthorizedPrincipalSource(AuthorizedPrincipal? current) : IAuthorizedPrincipalSource
{
    /// <summary>The identity a stubbed caller is admitted under, which nothing here decides access from.</summary>
    private const string StubIdentity = "stub-caller";

    /// <inheritdoc />
    public AuthorizedPrincipal? Current { get; } = current;

    /// <summary>Answers with a caller holding everything the MCP surface publishes, which is a deployment that narrowed nothing.</summary>
    /// <returns>The source to register.</returns>
    public static StubAuthorizedPrincipalSource GrantingTheWholeMailSurface() =>
        new(CallerHolding(MailFathomPermission.PublishedFor(ProtectedSurface.Mail)));

    /// <summary>Describes a caller granted exactly the permissions named.</summary>
    /// <param name="permissions">What the caller holds.</param>
    /// <returns>The principal to answer with.</returns>
    public static AuthorizedPrincipal CallerHolding(params IEnumerable<MailFathomPermission> permissions) =>
        AuthorizedPrincipal.Caller(StubIdentity, permissions);
}
