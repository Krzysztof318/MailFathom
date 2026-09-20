// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Tasks;

namespace MailFathom.AI.DayLayout;

/// <summary>Lays out nothing, on a deployment that declared no model to lay a day out with.</summary>
/// <remarks>
/// <para>
/// The default state of an instance, and what every deployment gets until a chat endpoint is declared. It exists as a
/// registration rather than as an absent one because the answer the use case needs is a reason it can act on, and a
/// missing service would have made it carry a null check and decide for itself what an absence meant.
/// </para>
/// <para>
/// Nothing is read and nothing is sent. The day is not touched at all, so an instance in this state cannot disclose a
/// task or an appointment however often it is asked, which is the property the switch exists to give and would not
/// have if the refusal came after the turn had been composed.
/// </para>
/// </remarks>
internal sealed class InactiveDayLayoutPlanner : IDayLayoutPlanner
{
    /// <summary>The one instance, since it holds nothing and answers everything the same way.</summary>
    internal static readonly InactiveDayLayoutPlanner Instance = new();

    private InactiveDayLayoutPlanner()
    {
    }

    /// <inheritdoc />
    public bool IsActive => false;

    /// <inheritdoc />
    public Task<DayLayoutDerivation> SuggestAsync(DayLayoutQuestion question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(DayLayoutDerivation.Withholding(DayLayoutWithholding.NotActivated));
    }
}
