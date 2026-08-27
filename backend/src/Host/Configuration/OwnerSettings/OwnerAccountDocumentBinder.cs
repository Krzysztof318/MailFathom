// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using MailFathom.Infrastructure.Persistence.Owners;
using MailFathom.Infrastructure.Persistence.Settings;
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

        // Two measurements of one bound, in this order. The first costs no allocation and is what keeps an absurd
        // string from being copied and walked at all; the second is the rule, because what the read enforces is the
        // rendering PostgreSQL stores rather than the compact form a candidate is composed as.
        var writtenOctets = Encoding.UTF8.GetByteCount(json);

        if (writtenOctets > OwnerSettingsDocument.MaximumOctets)
        {
            return OwnerAccountBinding.Refused([PastTheCeiling(writtenOctets)]);
        }

        int persistedOctets;

        try
        {
            persistedOctets = RootSettingsCommitRules.PersistedOctetsOf(json);
        }
        catch (JsonException)
        {
            return OwnerAccountBinding.Refused([NotADocument]);
        }

        if (persistedOctets > OwnerSettingsDocument.MaximumOctets)
        {
            return OwnerAccountBinding.Refused([PastTheCeiling(persistedOctets)]);
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
                // The parser's own sentence quotes the character it stopped on, which in a malformed record is as
                // likely to be a character of a mailbox password as of anything else, so what goes back is this
                // binder's sentence and the position stays where it was raised.
                return OwnerAccountBinding.Refused([NotADocument]);
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

    /// <summary>The one sentence a document nothing can parse is refused with, whichever stage met it.</summary>
    private const string NotADocument =
        "The owner record is not a JSON object MailFathom can read. An owner record is a document of settings, so check what wrote the settings_accounts row rather than the settings themselves.";

    /// <summary>Says a document is past the ceiling, naming what it measured and what the ceiling is.</summary>
    /// <remarks>The figure is the document as the database renders it, which is larger than the form it was written in.</remarks>
    private static string PastTheCeiling(int octets) =>
        $"The owner record is {octets} octets as the database stores it, past the {OwnerSettingsDocument.MaximumOctets} MailFathom binds an owner's document from. An owner record is a page of declarations rather than a payload: check what wrote the settings_accounts row.";

    /// <summary>Says what the strict binding refused, in a sentence about the record rather than about the binder.</summary>
    /// <remarks>
    /// <para>
    /// Neither sentence the framework raises can be handed on. The one naming unknown properties names MailFathom's
    /// own type and the binder option that was set, neither of which is a thing whoever wrote the record can act on;
    /// the one about a value that will not convert says nothing at all at the top and puts the setting's path in an
    /// inner failure that quotes <em>the value beside it</em> — which for a mailbox password is the material this
    /// binder refuses everywhere else. So the two shapes are recognized and re-stated, and the framework's own text
    /// is carried in neither arm nor in the fallback.
    /// </para>
    /// <para>
    /// The path is taken from the last marker rather than the first, because a value quoted before it may contain
    /// anything, including the marker itself. What comes out is then checked to be a configuration path — segments
    /// separated by colons and nothing else — so that a message shaped differently by a later runtime falls back to
    /// the general sentence instead of putting an unexamined fragment in front of a reader.
    /// </para>
    /// </remarks>
    private static string BindingRefusalFor(InvalidOperationException refusal)
    {
        const string unknownProperties = "were not found on the instance of";
        const string pathOpening = " at '";
        const string pathClosing = "' to type '";

        var message = refusal.Message;

        if (message.Contains(unknownProperties, StringComparison.Ordinal)
            && message.LastIndexOf(": ", StringComparison.Ordinal) is var named and > 0)
        {
            return $"The owner record names {message[(named + 2)..]}, which is not a setting an owner's record carries. Remove it, or correct the spelling of the setting it was meant to be.";
        }

        var conversion = refusal.InnerException?.Message ?? string.Empty;
        var closing = conversion.LastIndexOf(pathClosing, StringComparison.Ordinal);
        var opening = closing > 0 ? conversion.LastIndexOf(pathOpening, closing, StringComparison.Ordinal) : -1;

        if (opening > 0)
        {
            var path = conversion[(opening + pathOpening.Length)..closing];

            if (IsSettingPath(path))
            {
                return $"The value the owner record gives {path} is not of the type that setting takes. Correct it to the type the setting is declared as.";
            }
        }

        return "The owner record does not bind to an owner's settings. Check it against the settings an owner's record carries.";
    }

    /// <summary>Gets whether a fragment is a configuration path and therefore safe to repeat back.</summary>
    private static bool IsSettingPath(string candidate) =>
        candidate.Length > 0
        && candidate.Split(':').All(segment => segment.Length > 0 && segment.All(char.IsLetterOrDigit));

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
            return OwnerAccountBinding.Refused([BindingRefusalFor(refusal)]);
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
