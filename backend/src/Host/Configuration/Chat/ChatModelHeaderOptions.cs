// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Host.Configuration.Chat;

/// <summary>One header every request to a declared model carries beside whatever the credential writes.</summary>
/// <remarks>
/// <para>
/// The shape a gateway fronting several models asks for: a tenant, a project, or a routing key that decides which model
/// answers. It is declared per model rather than once for the section, because the gateway one model is reached through
/// is not the server another one is.
/// </para>
/// <para>
/// The value is a secret block rather than a string, and that is the whole reason this is a pair of keys instead of a
/// dictionary of them. What goes in one of these is a routing token or a tenant identifier — material of the same kind
/// as the key beside it — so it is resolved per request from a reference, kept out of the configuration file, and found
/// by the same discovery that finds every other secret this deployment holds.
/// </para>
/// </remarks>
internal sealed class ChatModelHeaderOptions
{
    /// <summary>Gets or sets the field name the value is sent under.</summary>
    /// <remarks>
    /// Refused unless it is a field name in the first place, and refused where it names one the request already owns:
    /// <c>Authorization</c> is the credential's, and the framing headers are the transport's, so a declaration writing
    /// one would either be overwritten without a word or would corrupt the request it travelled on.
    /// </remarks>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the reference the value is resolved from.</summary>
    /// <remarks>Absent by default rather than an empty block, so secret discovery does not find an unresolvable reference nobody wrote.</remarks>
    public ConfiguredSecret? Value { get; set; }

    /// <summary>Reports everything an operator must fix before this header could be sent.</summary>
    /// <param name="modelDescription">How a message names the model this header belongs to, already carrying its alias.</param>
    /// <returns>One result per rule the declaration breaks, empty when it is usable.</returns>
    public IEnumerable<ValidationResult> FindConfigurationErrors(string modelDescription)
    {
        var name = this.Name.Trim();

        if (name.Length == 0)
        {
            yield return new ValidationResult(
                $"{modelDescription} declares a header with no Name, so nothing says what the value would be sent as.",
                [nameof(this.Name)]);

            yield break;
        }

        if (!HttpFieldName.IsFieldName(name))
        {
            yield return new ValidationResult(
                $"{modelDescription} declares the header '{name}', which is not a field name. A field name carries letters, digits, and the characters !#$%&'*+-.^_`|~ and nothing else.",
                [nameof(this.Name)]);

            yield break;
        }

        if (HttpFieldName.IsWrittenByTheRequestItself(name))
        {
            yield return new ValidationResult(
                $"{modelDescription} declares the header '{name}', which the request writes for itself — the credential and the message framing are not an operator's to set here.",
                [nameof(this.Name)]);
        }

        if (this.Value is null)
        {
            yield return new ValidationResult(
                $"{modelDescription} declares the header '{name}' with no Value, so nothing says what to send under it. The value is a secret reference like every other credential here.",
                [nameof(this.Value)]);
        }
    }
}
