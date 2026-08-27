// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using MailFathom.Infrastructure.Persistence.Owners;
using Microsoft.Extensions.Configuration.Json;

namespace MailFathom.Host.Configuration.OwnerSettings;

/// <summary>Turns the document one owner's row holds into their typed record, or says why it is not one.</summary>
/// <remarks>
/// <para>
/// The order of the stages is the contract rather than an implementation detail. The document is bounded before it is
/// parsed, because a row past the ceiling is refused whatever it would have bound to and refusing it afterwards would
/// have paid the expansion the ceiling exists to refuse. Secret material is refused next, over the flattened keys,
/// because that answer depends on the values alone rather than on whether the record is otherwise valid — and it is
/// the one refusal that must not wait behind an unrelated typo, since material that reached the column is already
/// where it must not be. The binding is strict, so an unknown property is a refusal rather than a value quietly
/// discarded, and only a document surviving all of it is judged by the rules a mail account is declared under.
/// </para>
/// <para>
/// One binder rather than one per direction, which is the point of it. Whatever comes to read an owner's record and
/// whatever comes to accept a new one are both meant to arrive here, so the rules a candidate is judged by and the
/// rules a stored record is judged by cannot drift apart — there is one set of them. This release carries the binder
/// and no path that drives it: the reader beside it hands a document on as the row holds it, bounded by size and
/// judged in no other way.
/// </para>
/// <para>
/// Nothing here composes a configuration layer over the deployment's. The record is bound from the document alone, so
/// no value in it shadows a setting the deployment made, and an owner-level setting is only ever a property the record
/// declares.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this binder.")]
internal sealed class OwnerAccountDocumentBinder(PersistedSecretMaterial secretMaterial)
{
    /// <summary>Binds an owner's document and judges the record it produces.</summary>
    /// <param name="json">The owner's document, as the JSON object their row holds.</param>
    /// <returns>The bound record, or the sentences naming what must change first.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="json" /> is <see langword="null" />, empty, or white space, which is not a document at all.</exception>
    public OwnerAccountBinding Bind(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var octets = Encoding.UTF8.GetByteCount(json);

        if (octets > OwnerSettingsDocument.MaximumOctets)
        {
            return OwnerAccountBinding.Refused(
            [
                $"The owner record is {octets} octets, past the {OwnerSettingsDocument.MaximumOctets} MailFathom binds an owner's document from. An owner record is a page of declarations rather than a payload: check what wrote the settings_accounts row.",
            ]);
        }

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);

        // The root holds a provider it loaded, and one abandoned undisposed would leave a parsed document per record.
        // Released in a finally rather than by a using declaration, so that the parse alone is what the refusal below
        // catches: a binding failure raised while the record is judged is a different answer and must not read as a
        // document nobody could parse.
        ConfigurationRoot? document = null;

        try
        {
            try
            {
                document = Read(stream);
            }
            catch (Exception refusal) when (refusal is FormatException or JsonException)
            {
                // The parser's own sentence names the position it stopped at and not the text it was reading, which is
                // what makes it safe to hand back to whoever wrote the document.
                return OwnerAccountBinding.Refused([$"The owner record is not a JSON object MailFathom can read: {refusal.Message}"]);
            }

            return this.FindMaterialWrittenWhereAReferenceBelongs(document) is { Count: > 0 } material
                ? OwnerAccountBinding.Refused(material)
                : Judge(document);
        }
        finally
        {
            document?.Dispose();
        }
    }

    /// <summary>Reads the document into flattened configuration keys.</summary>
    /// <remarks>
    /// Named by the built type rather than by the interface, because what comes back is owned. Built from the provider
    /// rather than through the builder for the reason a candidate configuration is: the root's constructor loads each
    /// provider with no try of its own, so a builder-built root would drop what it had already constructed when the
    /// parse refuses.
    /// </remarks>
    private static ConfigurationRoot Read(Stream json) =>
        new([new JsonStreamConfigurationSource { Stream = json }.Build(new ConfigurationBuilder())]);

    /// <summary>Binds the document strictly and puts the record through the rules a mail account is declared under.</summary>
    private static OwnerAccountBinding Judge(IConfiguration document)
    {
        OwnerAccountOptions owner;

        try
        {
            owner = document.Get<OwnerAccountOptions>(binder => binder.ErrorOnUnknownConfiguration = true)
                ?? new OwnerAccountOptions();
        }
        catch (InvalidOperationException refusal)
        {
            // The framework's own sentence and never the inner failure, which is where a value would be.
            return OwnerAccountBinding.Refused([refusal.Message]);
        }

        var refusals = new List<ValidationResult>();

        Validator.TryValidateObject(owner, new ValidationContext(owner), refusals, validateAllProperties: true);

        return refusals.Count > 0
            ? OwnerAccountBinding.Refused([.. refusals.Select(refusal => refusal.ErrorMessage ?? "The owner record is invalid.")])
            : OwnerAccountBinding.Bound(owner);
    }

    /// <summary>Finds every setting of the document carrying a secret's material where a reference belongs.</summary>
    /// <remarks>
    /// Every one of them is reported rather than the first, because an owner correcting one at a time would learn
    /// about the next only by writing the record again. The message names the setting and says what belongs there, and
    /// repeats neither the value nor its length, because a length is what turns a guess about a credential into a
    /// shorter list of guesses.
    /// </remarks>
    private IReadOnlyList<string> FindMaterialWrittenWhereAReferenceBelongs(IConfiguration document) =>
    [
        .. document.AsEnumerable()
            .Where(setting => secretMaterial.IsCarriedBy(setting.Key, setting.Value))
            .Select(setting =>
                $"MailFathom does not persist secret material: {setting.Key} carries the value itself rather than a <scheme>:<target> reference this deployment resolves. Provision the secret and persist the reference to it."),
    ];
}
