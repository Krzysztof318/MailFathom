// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Infrastructure.Persistence.Owners;
using MailFathom.Infrastructure.Persistence.Settings;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Options;

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
/// rules a stored record is judged by cannot drift apart — there is one set of them, and
/// <see cref="OwnerRecordArrival" /> names the one rule that is not in it and why.
/// </para>
/// <para>
/// Nothing here composes a configuration layer over the deployment's. The record is bound from the document alone, so
/// no value in it shadows a setting the deployment made, and an owner-level setting is only ever a property the record
/// declares. The deployment's own section reaches this for one purpose and no other: to say what a record being
/// written may ask for. A scanning posture is refused there when it would switch off what the deployment requires or
/// reach for an analyzer the deployment never stood up, which is a rule about the record rather than a value composed
/// over it.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this binder.")]
internal sealed class OwnerAccountDocumentBinder(
    PersistedSecretMaterial secretMaterial,
    TimeProvider timeProvider,
    IOptions<SensitiveContentOptions> sensitiveContent)
{
    /// <summary>Binds an owner's document and judges the record it produces.</summary>
    /// <param name="json">The owner's document, as the JSON object their row holds.</param>
    /// <param name="arrival">Whether this document is being written or is one the deployment already holds.</param>
    /// <returns>The bound record, or the sentences naming what must change first.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="json" /> is <see langword="null" />, empty, or white space, which is not a document at all.</exception>
    public OwnerAccountBinding Bind(string json, OwnerRecordArrival arrival)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        // Two measurements of one bound, in this order. The first costs no allocation and is what keeps an absurd
        // string from being copied and walked at all; the second is the rule, because what the read enforces is the
        // rendering PostgreSQL stores rather than the compact form a candidate is composed as.
        var writtenOctets = Encoding.UTF8.GetByteCount(json);

        if (writtenOctets > OwnerSettingsDocument.MaximumOctets)
        {
            return OwnerAccountBinding.Refused(
            [
                $"The owner record is {writtenOctets} octets as it was written, past the {OwnerSettingsDocument.MaximumOctets} MailFathom binds an owner's document from — and the database stores it larger still. An owner record is a page of declarations rather than a payload: check what wrote the settings_accounts row.",
            ]);
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
                : this.Judge(document, arrival);
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
    /// <remarks>
    /// The figure is the document as the database renders it, which is larger than the form it was written in. The
    /// guard in front of the measurement says the same thing about the written form in its own words, because a
    /// figure under a sentence naming the other measurement would understate what the column holds.
    /// </remarks>
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
    /// anything, including the marker itself. Neither fragment goes back unexamined: the path has to be a
    /// configuration path — segments separated by colons and nothing else — and the property names have to be the
    /// quoted list the framework writes, carrying no control character. What fails either test falls back to the
    /// general sentence, which is also what a message shaped differently by a later runtime gets.
    /// </para>
    /// </remarks>
    private static string BindingRefusalFor(InvalidOperationException refusal)
    {
        const string unknownProperties = "were not found on the instance of";
        const string pathOpening = " at '";
        const string pathClosing = "' to type '";

        var message = refusal.Message;

        if (message.Contains(unknownProperties, StringComparison.Ordinal)
            && message.LastIndexOf(": ", StringComparison.Ordinal) is var named and > 0
            && QuotedNamesIn(message[(named + 2)..]) is { } names)
        {
            return $"The owner record names {names}, which is not a setting an owner's record carries. Remove it, or correct the spelling of the setting it was meant to be.";
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

    /// <summary>Gets the named properties back when they are safe to repeat, and nothing when they are not.</summary>
    /// <remarks>
    /// What sits in this fragment is the JSON property names of a <c>settings_accounts</c> row, which is text whoever
    /// wrote the row chose. A name carrying a newline would put a line of its own choosing into the refusal an
    /// administrator reads and into any log of it, and a name carrying the marker this fragment was cut at would leave
    /// the cut inside the name, so what came back would be a fragment the record does not hold. The framework quotes
    /// each name, which is what makes the second detectable: a fragment cut inside a name no longer opens with the
    /// quotation mark. Anything that fails either test goes back as nothing and the general sentence answers instead,
    /// and the bound is on the whole fragment rather than on each name because a page of names is as unreadable as one
    /// long one.
    /// </remarks>
    private static string? QuotedNamesIn(string candidate) =>
        candidate.Length is > 1 and <= 512
        && candidate.StartsWith('\'')
        && candidate.EndsWith('\'')
        && !candidate.Any(char.IsControl)
            ? candidate
            : null;

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
    private OwnerAccountBinding Judge(IConfiguration document, OwnerRecordArrival arrival)
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

        // The rules the bound graph cannot reach, each supplied what it needs from outside the record for the reason
        // the deployment's own section supplies a clock through a custom validator: a record judged by every rule but
        // these would accept a synchronization bound that the identical declaration in configuration refuses at
        // startup, and a scanning posture the deployment could not honour.
        refusals.AddRange(owner.FindSynchronizationWindowErrors(
            DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime)));

        if (arrival == OwnerRecordArrival.BeingWritten)
        {
            refusals.AddRange(owner.FindSensitiveContentErrors(sensitiveContent.Value));
        }

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
    /// <para>
    /// A property name the parser took verbatim is not necessarily a path — a record holding <c>{"": 1}</c> flattens
    /// to a section named by an empty string — so a key that names nothing is passed over here and refused by the
    /// strict binding as a property nothing binds, which is the refusal this class answers with rather than the
    /// argument failure the rule would raise on it.
    /// </para>
    /// <para>
    /// A key that does name something is still an owner's own text, and the rule this scan asks reads the last
    /// segment alone, so an earlier segment may carry a newline and a forged sentence. It is repeated only where every
    /// segment of it is a setting's name; otherwise the refusal says what was found and names no path, which is what
    /// leaves an administrator reading MailFathom's own words rather than the record's.
    /// </para>
    /// </remarks>
    private IReadOnlyList<string> FindMaterialWrittenWhereAReferenceBelongs(IConfiguration document) =>
    [
        .. document.AsEnumerable()
            .Where(setting =>
                !string.IsNullOrWhiteSpace(setting.Key) && secretMaterial.IsCarriedBy(setting.Key, setting.Value))
            .Select(setting => IsSettingPath(setting.Key)
                ? $"MailFathom does not persist secret material: {setting.Key} carries the value itself rather than a <scheme>:<target> reference this deployment resolves. Provision the secret and persist the reference to it."
                : "MailFathom does not persist secret material: a setting of the owner record carries the value itself rather than a <scheme>:<target> reference this deployment resolves. Provision the secret and persist the reference to it."),
    ];
}
