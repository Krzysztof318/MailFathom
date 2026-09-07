// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.TestSupport;

/// <summary>The users a test arranges when which user it is does not matter, only that there are two of them.</summary>
/// <remarks>
/// Two fixed identities rather than generated ones, so a failure names the same value every run and a test asserting a
/// refusal can be read without tracing where the identifier came from. They are stated here rather than per suite
/// because the whole of what most of these tests need is one user and somebody else.
/// </remarks>
internal static class SyntheticMailUser
{
    /// <summary>Gets the user a deployment serves, which every configured mail account belongs to.</summary>
    public static MailUserId Deployment { get; } = MailUserId.Create(new Guid("11111111-1111-1111-1111-111111111111"));

    /// <summary>Gets a user this deployment does not serve, whose accounts nothing admitted here may reach.</summary>
    public static MailUserId Another { get; } = MailUserId.Create(new Guid("22222222-2222-2222-2222-222222222222"));
}
