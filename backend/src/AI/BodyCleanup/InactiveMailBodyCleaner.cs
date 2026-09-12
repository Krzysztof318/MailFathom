// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Cleaning;

namespace MailFathom.AI.BodyCleanup;

/// <summary>Cleans nothing, on a deployment that has not turned the third rendering on.</summary>
/// <remarks>
/// <para>
/// The default state of an instance, and what every deployment gets until an operator says otherwise. It is a
/// registration rather than an absent one because the reading pane needs a reason it can draw, and a missing service
/// would have made the route carry a null check and decide for itself what an absence meant.
/// </para>
/// <para>
/// Nothing is read and nothing is sent. The outline is not looked at at all, so an instance in this state cannot disclose
/// a message however often it is asked — which is the property the switch exists to give and would not have if the
/// refusal came after a turn had been composed.
/// </para>
/// </remarks>
internal sealed class InactiveMailBodyCleaner : IMailBodyCleaner
{
    /// <summary>The one instance, since it holds nothing and answers everything the same way.</summary>
    internal static readonly InactiveMailBodyCleaner Instance = new();

    private InactiveMailBodyCleaner()
    {
    }

    /// <inheritdoc />
    public bool IsActive => false;

    /// <inheritdoc />
    public Task<MailBodyCleaningProposal> ProposeAsync(
        CleanableMailBody body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(MailBodyCleaningProposal.Withheld(MailBodyCleaningWithholding.NotActivated));
    }
}
