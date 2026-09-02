// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Mail.Delivery.Addressing;

/// <summary>Reads the three recipient headers a boundary carries into the one list every author writes.</summary>
/// <remarks>
/// It sits here rather than beside one protocol's argument readers because more than one boundary now writes mail: a
/// tool call and a client request carry the same three lists and must read them the same way, and a second reading
/// would be a second answer to how many people a message names and to what a blank entry means.
/// </remarks>
public static class AuthoredRecipientHeaders
{
    /// <summary>Collects the three headers into the one recipient list every author writes.</summary>
    /// <param name="to">The addresses named in the <c>To</c> header, or <see langword="null" /> where the act names none.</param>
    /// <param name="cc">The addresses named in the <c>Cc</c> header, or <see langword="null" />.</param>
    /// <param name="bcc">The addresses named in the <c>Bcc</c> header, or <see langword="null" />.</param>
    /// <param name="tooManyRecipients">Raises the caller's own refusal for a list longer than a record holds.</param>
    /// <param name="fieldUnusable">Raises the caller's own refusal for a header carrying an entry that names nobody.</param>
    /// <returns>The recipients the author named, in the order the headers are read in.</returns>
    /// <remarks>
    /// <para>
    /// The order is the order the headers are read in, which is the order the composition writes them in. Nothing is
    /// deduplicated or parsed here: whether text names a mailbox is the composition's question, and how many people a
    /// message may actually reach is the deployment's number, both asked once for every way a message is authored.
    /// What is answered here is only how long the caller's own lists are, because that is what decides whether they are
    /// expanded at all, and the check therefore belongs in front of the expansion rather than after it.
    /// </para>
    /// <para>
    /// A send and a draft read the same headers and refuse the same values, and differ only in the failure they raise:
    /// a caller told its <em>submission</em> was refused while saving a draft would read that as a message having been
    /// offered to a server and turned down, which is the one thing that certainly did not happen. So the act supplies
    /// the two refusals rather than the reading being written twice, and the code and the sentence stay the same either
    /// way because both are published from one place.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="tooManyRecipients" /> or <paramref name="fieldUnusable" /> is <see langword="null" />.</exception>
    /// <exception cref="MailFathomException">Thrown as whichever refusal the act supplied, when the three headers name more people than a record holds or an entry carries no address.</exception>
    public static IReadOnlyList<NamedRecipient> NamedRecipients(
        IReadOnlyList<string>? to,
        IReadOnlyList<string>? cc,
        IReadOnlyList<string>? bcc,
        Func<MailFathomException> tooManyRecipients,
        Func<AuthoredEmailRefusal, MailFathomException> fieldUnusable)
    {
        ArgumentNullException.ThrowIfNull(tooManyRecipients);
        ArgumentNullException.ThrowIfNull(fieldUnusable);

        if (Counted(to, cc, bcc) > OutgoingEmailRequest.MaximumRecipientCount)
        {
            throw tooManyRecipients();
        }

        return Collect(to, cc, bcc, out var unusableField)
            ?? throw fieldUnusable(new AuthoredEmailRefusal(AuthoredEmailRefusalReason.FieldUnusable, unusableField));
    }

    /// <summary>Counts what the three headers name together, which is what decides whether they are expanded at all.</summary>
    private static int Counted(
        IReadOnlyList<string>? to,
        IReadOnlyList<string>? cc,
        IReadOnlyList<string>? bcc) =>
        (to?.Count ?? 0) + (cc?.Count ?? 0) + (bcc?.Count ?? 0);

    /// <summary>Reads the three headers into one list, or reports the header carrying an entry that names nobody.</summary>
    /// <returns>The recipients, or <see langword="null" /> when an entry carried no address.</returns>
    /// <remarks>
    /// Blank text stops the reading because an authored recipient is built from an address and a blank one names
    /// nothing to build from — a defect in whoever called rather than an author's mistake, and this is the boundary
    /// that keeps it from becoming one. Everything else the text may be wrong about travels unparsed to the
    /// composition, which is the single place an address is read and refused.
    /// </remarks>
    private static List<NamedRecipient>? Collect(
        IReadOnlyList<string>? to,
        IReadOnlyList<string>? cc,
        IReadOnlyList<string>? bcc,
        out AuthoredEmailField unusableField)
    {
        var named = new List<NamedRecipient>(Counted(to, cc, bcc));

        if (AddNamed(named, to, OutgoingRecipientRole.To, AuthoredEmailField.To, out unusableField)
            && AddNamed(named, cc, OutgoingRecipientRole.Cc, AuthoredEmailField.Cc, out unusableField)
            && AddNamed(named, bcc, OutgoingRecipientRole.Bcc, AuthoredEmailField.Bcc, out unusableField))
        {
            return named;
        }

        return null;
    }

    /// <summary>Adds one header's addresses, stopping at an entry that names nobody at all.</summary>
    /// <returns><see langword="true" /> when every entry named somebody.</returns>
    private static bool AddNamed(
        List<NamedRecipient> named,
        IReadOnlyList<string>? addresses,
        OutgoingRecipientRole role,
        AuthoredEmailField field,
        out AuthoredEmailField unusableField)
    {
        unusableField = field;

        if (addresses is null)
        {
            return true;
        }

        foreach (var address in addresses)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return false;
            }

            named.Add(NamedRecipient.AtAddress(role, address));
        }

        return true;
    }
}
