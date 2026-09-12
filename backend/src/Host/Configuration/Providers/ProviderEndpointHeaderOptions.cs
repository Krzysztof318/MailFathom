// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Host.Configuration.Providers;

/// <summary>One header every request to a declared endpoint carries beside whatever the credential writes.</summary>
/// <remarks>
/// <para>
/// The shape a gateway fronting several models asks for: a tenant, a project, or a routing key that decides which model
/// answers. It is declared per endpoint rather than once for a section, because the gateway one model is reached
/// through is not the server another one is.
/// </para>
/// <para>
/// Declared by a chat model and by an embedding endpoint alike, and read here rather than once per section, because
/// what a header may be is a property of the request rather than of the role the request serves. A rule stated twice
/// would be a rule the two sections could come to disagree about.
/// </para>
/// <para>
/// The value is a secret block rather than a string, and that is the whole reason this is a pair of keys instead of a
/// dictionary of them. What goes in one of these is a routing token or a tenant identifier — material of the same kind
/// as the key beside it — so it is resolved per request from a reference, kept out of the configuration file, and found
/// by the same discovery that finds every other secret this deployment holds.
/// </para>
/// </remarks>
internal sealed class ProviderEndpointHeaderOptions
{
    /// <summary>The name both sections bind this list under, which a reported key is written with.</summary>
    /// <remarks>A constant rather than a <c>nameof</c>, because the property it names belongs to whichever block declared the list and this type is read by both.</remarks>
    private const string CollectionName = "ExtraHeaders";

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

    /// <summary>Reports everything an operator must fix before the headers one endpoint declared could be sent.</summary>
    /// <param name="endpointDescription">How a message names the endpoint these headers belong to, already carrying its alias.</param>
    /// <param name="declarations">The declared headers, in the order they were written.</param>
    /// <returns>One result per rule the list breaks, empty when every declaration is usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declarations" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Each result is keyed to the element it came from, because a header's rules name <c>Name</c> or <c>Value</c> and
    /// an operator sent to a block's own <c>Name</c> would be sent to a key nothing binds. Whichever block declared the
    /// list prefixes its own position on top of that where it has one.
    /// </remarks>
    public static IEnumerable<ValidationResult> FindConfigurationErrors(
        string endpointDescription,
        IReadOnlyList<ProviderEndpointHeaderOptions> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);

        var declared = declarations.SelectMany((header, index) => header
            .FindDeclarationErrors(endpointDescription)
            .Select(error => KeyedToThisHeader(error, index)));

        foreach (var error in declared)
        {
            yield return error;
        }

        // A repeated name is not two headers: the last one written wins and the others are paid for and discarded, so
        // an operator who meant to send both learns it from a request that carried one of them.
        var repeated = declarations
            .Select(header => header.Name.Trim())
            .Where(name => name.Length > 0)
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        foreach (var name in repeated)
        {
            yield return new ValidationResult(
                $"{endpointDescription} declares the header '{name}' more than once. A field name is sent once, so the repetitions would be resolved and discarded.",
                [CollectionName]);
        }
    }

    private static ValidationResult KeyedToThisHeader(ValidationResult error, int index) => new(
        error.ErrorMessage,
        [.. error.MemberNames.Select(member => $"{CollectionName}:{index}:{member}")]);

    private IEnumerable<ValidationResult> FindDeclarationErrors(string endpointDescription)
    {
        var name = this.Name.Trim();

        if (name.Length == 0)
        {
            yield return new ValidationResult(
                $"{endpointDescription} declares a header with no Name, so nothing says what the value would be sent as.",
                [nameof(this.Name)]);

            yield break;
        }

        if (!HttpFieldName.IsFieldName(name))
        {
            yield return new ValidationResult(
                $"{endpointDescription} declares the header '{name}', which is not a field name. A field name carries letters, digits, and the characters !#$%&'*+-.^_`|~ and nothing else.",
                [nameof(this.Name)]);

            yield break;
        }

        if (HttpFieldName.IsWrittenByTheRequestItself(name))
        {
            yield return new ValidationResult(
                $"{endpointDescription} declares the header '{name}', which the request writes for itself — the credential and the message framing are not an operator's to set here.",
                [nameof(this.Name)]);
        }

        if (this.Value is null)
        {
            yield return new ValidationResult(
                $"{endpointDescription} declares the header '{name}' with no Value, so nothing says what to send under it. The value is a secret reference like every other credential here.",
                [nameof(this.Value)]);
        }
    }
}
