// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Mail.Delivery.Authoring;

/// <summary>Carries the answer that was authored, or the reason none was.</summary>
/// <remarks>
/// <para>
/// What comes back is an ordinary authored message. There is no reply-shaped contract below this point and no second
/// path into composition: the threading identifiers ride on the authored message like every other decision, so a reply,
/// a forward, and a message answering nothing are composed by the same code and are bounded by the same numbers.
/// </para>
/// <para>
/// A refusal is a result rather than an exception for the reason a composition refusal is: the caller acts on it
/// directly and nothing was written down, so the answer simply does not exist.
/// </para>
/// </remarks>
public sealed record AuthoredResponse
{
    private AuthoredResponse(MailAccountIdentity account, AuthoredEmail? email, AuthoredResponseRefusal? refusal)
    {
        this.Account = account;
        this.Email = email;
        this.Refusal = refusal;
    }

    /// <summary>Gets the account the answer is sent as, which is the account the answered email was stored from.</summary>
    /// <remarks>
    /// It is resolved here rather than named by the caller, and that is what keeps a reply on the mailbox it belongs
    /// to: answering from a second configured account would write the exchange as somebody the correspondent has never
    /// heard from. It is the default value on a refusal, which carries no account for the same reason it carries no
    /// address.
    /// </remarks>
    public MailAccountIdentity Account { get; }

    /// <summary>Gets the identifier half of <see cref="Account" />, which is what code already narrowed to one owner names.</summary>
    public MailAccountId AccountId => this.Account.Id;

    /// <summary>Gets the authored message, or <see langword="null" /> when the answer was refused.</summary>
    public AuthoredEmail? Email { get; }

    /// <summary>Gets why no answer was authored, or <see langword="null" /> when one was.</summary>
    public AuthoredResponseRefusal? Refusal { get; }

    /// <summary>Gets whether an answer was authored.</summary>
    public bool IsAuthored => this.Email is not null;

    /// <summary>Reports the answer somebody wrote to a stored email.</summary>
    /// <param name="account">The account the answer is sent as, named by its owner and its identifier.</param>
    /// <param name="email">The authored message.</param>
    /// <returns>An authored result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="email" /> is <see langword="null" />.</exception>
    public static AuthoredResponse Authored(MailAccountIdentity account, AuthoredEmail email)
    {
        ArgumentNullException.ThrowIfNull(email);

        return new AuthoredResponse(account, email, refusal: null);
    }

    /// <summary>Reports that no answer was authored, and why.</summary>
    /// <param name="reason">What stopped it.</param>
    /// <param name="bound">The number that was exceeded, when the reason is a bound.</param>
    /// <returns>A refused result.</returns>
    public static AuthoredResponse Refused(AuthoredResponseRefusalReason reason, long? bound = null) =>
        new(default, email: null, new AuthoredResponseRefusal(reason, bound));

    /// <summary>Reports that a recipient the author added addressed nobody, in this result's own terms.</summary>
    /// <param name="refusal">Why the recipient resolved to nobody.</param>
    /// <returns>A refused result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="refusal" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the resolution reason is not one this system declares, which a refusal built from a cast integer is.</exception>
    /// <remarks>
    /// The reason is translated rather than carried, because an author of an answer acts on one refusal shape whatever
    /// part of the send produced it. The two identities stay the same on the other side of the translation: each reason
    /// maps to the code the resolution itself publishes.
    /// </remarks>
    public static AuthoredResponse Refused(RecipientResolutionRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);

        var reason = refusal.Reason switch
        {
            RecipientResolutionRefusalReason.ContactUnknown =>
                AuthoredResponseRefusalReason.RecipientContactUnknown,
            RecipientResolutionRefusalReason.ContactNameAmbiguous =>
                AuthoredResponseRefusalReason.RecipientContactNameAmbiguous,
            RecipientResolutionRefusalReason.ContactAddressNotHeld =>
                AuthoredResponseRefusalReason.RecipientContactAddressNotHeld,
            _ => throw new InvalidOperationException(
                "The recipient resolution refusal reason is not one this system declares."),
        };

        return new AuthoredResponse(
            default,
            email: null,
            new AuthoredResponseRefusal(reason, Bound: null, refusal.MatchedContactCount));
    }
}
