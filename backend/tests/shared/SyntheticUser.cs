// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.TestSupport;

/// <summary>The users a test arranges when which user it is does not matter, only that they are different people.</summary>
/// <remarks>
/// Fixed identities rather than generated ones, so a failure names the same value every run and a test asserting a
/// refusal can be read without tracing where the identifier came from. They are stated here rather than per suite
/// because the whole of what most of these tests need is one user and somebody else — and, where a mailbox is shared,
/// two people who reach it and a third who does not.
/// </remarks>
internal static class SyntheticUser
{
    /// <summary>Gets the user a deployment serves, which every configured mail account belongs to.</summary>
    public static UserId Deployment { get; } = UserId.Create(new Guid("11111111-1111-1111-1111-111111111111"));

    /// <summary>Gets a user this deployment does not serve, whose accounts nothing admitted here may reach.</summary>
    public static UserId Another { get; } = UserId.Create(new Guid("22222222-2222-2222-2222-222222222222"));

    /// <summary>Gets a third user, for the claim a pair cannot make: that a mailbox two people share reaches nobody else.</summary>
    /// <remarks>
    /// Two users are enough to state that one cannot read the other's mail. They are not enough to state that a
    /// mailbox assigned to both is still assigned to somebody rather than to everybody, because every user in the
    /// arrangement reaches it — which is what this one is here to be excluded from.
    /// </remarks>
    public static UserId Third { get; } = UserId.Create(new Guid("33333333-3333-3333-3333-333333333333"));
}
