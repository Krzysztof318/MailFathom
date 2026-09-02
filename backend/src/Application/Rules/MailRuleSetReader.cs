// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Rules;

/// <summary>Reports the rule set in force to whoever may read it.</summary>
/// <remarks>
/// <para>
/// A pass reads <see cref="IMailRuleSetSource" /> directly and holds what it was given, which is the reload contract and
/// is not what this is for. This is the operator's reading: what a caller is being told is what this deployment will do
/// to a mailbox, which is a report of the deployment's own state and carries no mail — so it asks for the read grant,
/// while what the rules concluded about somebody's messages is <see cref="History.MailRuleHistory" /> and a different
/// one.
/// </para>
/// <para>
/// The reading is not asynchronous, and the rule set itself cannot fail to be produced, for the reason the source it
/// reads gives: a candidate rule set that could not be proven usable never becomes the current one. What the reading
/// does refuse is a caller whose grant does not carry the permission, which it raises rather than reports.
/// </para>
/// </remarks>
public sealed class MailRuleSetReader
{
    private readonly IMailRuleSetSource ruleSets;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the reading over the published rule set.</summary>
    /// <param name="ruleSets">Hands out the rule set compiled from the published configuration.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailRuleSetReader(IMailRuleSetSource ruleSets, AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(ruleSets);
        ArgumentNullException.ThrowIfNull(authorization);

        this.ruleSets = ruleSets;
        this.authorization = authorization;
    }

    /// <summary>Reads the rule set a pass starting now would run against.</summary>
    /// <returns>The loaded rule set.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminRead" />.</exception>
    public MailRuleSet Read()
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminRead);

        return this.ruleSets.Current;
    }
}
