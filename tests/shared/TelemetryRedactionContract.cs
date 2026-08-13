// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace MailFathom.TestSupport;

/// <summary>Holds the contract every MailFathom span and instrument obeys, and asserts it over an emitted surface.</summary>
/// <remarks>
/// <para>
/// The rule itself is one sentence: a signal carries counts, sizes, durations, outcomes, error codes, and MailFathom's
/// own configured account, folder, and endpoint aliases, and nothing else. Mail content, an address, a subject, a
/// remote folder path, a message identifier, a UID, a search term, a credential, and model prompt or completion text
/// are refused — which is a cardinality rule as much as a privacy one, because every one of them would open a time
/// series per message or per person.
/// </para>
/// <para>
/// Asserting it needs two different mechanisms, because names and values fail differently. A <b>name</b> — a span's, an
/// instrument's, a dimension's — is a literal somebody wrote, so it is judged by reading it: the vocabulary below names
/// what a name may never be about, and the shapes name what one may look like. A <b>value</b> is whatever ran, so no
/// list decides it; the driving test poisons every string a caller or a message could supply and this asserts where
/// each poison was allowed to surface. Between them they say what the acceptance asks: nothing about mail or a person
/// is in a name, and no dimension takes its value from one.
/// </para>
/// <para>
/// This file holds no inventory of the names that exist today. An approved list would fail on every addition rather
/// than on every wrong one, which trains a reader to extend it without reading it — and the surface it would mirror is
/// documented in <c>docs/operations/telemetry.md</c>, where a person reads it. What is asserted here is the property,
/// so a signal nobody has written yet is covered by it.
/// </para>
/// </remarks>
internal static class TelemetryRedactionContract
{
    /// <summary>Stands in for an alias an operator configured, which is the one caller string a dimension may carry.</summary>
    internal const string ConfiguredAliasSentinel = "sentinel-configured-alias";

    /// <summary>Stands in for text a caller sent — a tool name, a protocol argument — which no dimension may carry.</summary>
    internal const string CallerSuppliedSentinel = "sentinel-caller-supplied";

    /// <summary>Stands in for anything read out of a message, which reaches no name, key, or value anywhere.</summary>
    internal const string MailDerivedSentinel = "sentinel-mail-derived";

    /// <summary>The dimensions permitted to carry a name an operator configured, and the only ones.</summary>
    /// <remarks>
    /// An alias is MailFathom's own word for an account, a folder, or a provider endpoint: the operator wrote it, it is
    /// bounded by the size of their configuration, and a dashboard is unreadable without it. Every other dimension is
    /// one of this process's own closed words, so a caller string arriving on one is the defect this catches.
    /// </remarks>
    internal static readonly string[] DimensionsCarryingAConfiguredAlias =
    [
        "mailfathom.mail.account",
        "mailfathom.mail.folder",
        "mailfathom.mail.folder_alias",
        "mailfathom.answering.endpoint",
    ];

    /// <summary>The words a span, an instrument, or a dimension may never be named after.</summary>
    /// <remarks>
    /// <para>
    /// Each one names either a piece of a message or a secret, so a signal named after it is already wrong whatever it
    /// happens to carry at run time. The match is over whole segments — a name is split on <c>.</c> and <c>_</c> — so
    /// that a word only fails where it is the thing being named. That is what separates <c>token</c>, which names a
    /// credential, from <c>tokens</c>, which is how many a model consumed and is a legitimate count; and it is what
    /// keeps <c>stored_total</c> from tripping over a two-letter word inside it.
    /// </para>
    /// <para>
    /// <c>content</c> is deliberately absent. It is the name of a subsystem here — the content store, the byte volume it
    /// moves — rather than of a message's text, and forbidding it would forbid the instruments that report how much
    /// storage a mailbox costs.
    /// </para>
    /// </remarks>
    internal static readonly string[] WordsNoSignalIsNamedAfter =
    [
        "subject",
        "sender",
        "recipient",
        "address",
        "uid",
        "uidvalidity",
        "body",
        "snippet",
        "text",
        "query",
        "prompt",
        "completion",
        "password",
        "secret",
        "credential",
        "token",
        "capability",
        "path",
        "filename",
        "id",
        "identifier",
    ];

    /// <summary>The shape an instrument name or a dimension key takes, which is one namespace under one root.</summary>
    private static readonly Regex DimensionShape = new(
        @"^mailfathom(\.[a-z0-9]+(_[a-z0-9]+)*)+$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>The shape a span name takes, which is the operation it reports written as one lower-case phrase.</summary>
    /// <remarks>
    /// A hyphen is allowed beside an underscore because one family of spans is named after the mutation it performs,
    /// and a mutation's published name is hyphenated — <c>set-seen</c> is the word an operator already reads in the
    /// audit trail and in the configuration, so the span agreeing with it is worth more than one spelling rule.
    /// </remarks>
    private static readonly Regex SpanNameShape = new(
        @"^[a-z][a-z0-9]*([_-][a-z0-9]+)*$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly char[] NameSeparators = ['.', '_', '-'];

    /// <summary>Asserts that nothing on the emitted surface is named after mail, a person, or a secret.</summary>
    /// <param name="emittedNames">Every span name, instrument name, and dimension key the publishers emitted.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="emittedNames" /> is <see langword="null" />.</exception>
    internal static void AssertNothingIsNamedAfterMailOrASecret(IReadOnlyCollection<string> emittedNames)
    {
        ArgumentNullException.ThrowIfNull(emittedNames);

        string[] offending =
        [
            .. emittedNames
                .Where(name => SegmentsOf(name).Intersect(WordsNoSignalIsNamedAfter, StringComparer.Ordinal).Any())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Empty(offending);
    }

    /// <summary>Asserts that every instrument name and dimension key is one namespaced lower-case word list.</summary>
    /// <param name="instrumentNames">Every instrument that appeared on MailFathom's meter.</param>
    /// <param name="emittedTags">Every tag a span or a measurement carried.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The shape is what keeps a dimension from being minted out of a value. A key assembled at run time — from an
    /// account, a header name, a rule — would not survive it, and a key written by hand in the wrong namespace would
    /// leave the one filter an operator has for everything this process owns.
    /// </remarks>
    internal static void AssertEveryDimensionIsNamespacedUnderMailFathom(
        IReadOnlyCollection<string> instrumentNames,
        IReadOnlyCollection<KeyValuePair<string, object?>> emittedTags)
    {
        ArgumentNullException.ThrowIfNull(instrumentNames);
        ArgumentNullException.ThrowIfNull(emittedTags);

        string[] offending =
        [
            .. instrumentNames
                .Concat(emittedTags.Select(tag => tag.Key))
                .Distinct(StringComparer.Ordinal)
                .Where(name => !DimensionShape.IsMatch(name))
                .Order(StringComparer.Ordinal),
        ];

        Assert.Empty(offending);
    }

    /// <summary>Asserts that every span is named after the operation it reports rather than after anything it saw.</summary>
    /// <param name="spanNames">The name of every span the publishers started.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="spanNames" /> is <see langword="null" />.</exception>
    internal static void AssertEverySpanIsNamedAfterItsOperation(IReadOnlyCollection<string> spanNames)
    {
        ArgumentNullException.ThrowIfNull(spanNames);

        string[] offending =
        [
            .. spanNames
                .Distinct(StringComparer.Ordinal)
                .Where(name => !SpanNameShape.IsMatch(name))
                .Order(StringComparer.Ordinal),
        ];

        Assert.Empty(offending);
    }

    /// <summary>Asserts that the poisoned strings the drive supplied surfaced only where the contract allows.</summary>
    /// <param name="emittedNames">Every span name, instrument name, and dimension key the publishers emitted.</param>
    /// <param name="emittedTags">Every tag a span or a measurement carried.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// This is the half no list of names can do. Every string the drive handed a publisher is one of the three
    /// sentinels, so wherever one comes out is exactly where that class of input reaches an exporter: a caller's text
    /// and anything read out of a message may reach nowhere at all, and a configured alias may reach the dimensions
    /// named for it and no others.
    /// </remarks>
    internal static void AssertNoPoisonedInputEscaped(
        IReadOnlyCollection<string> emittedNames,
        IReadOnlyCollection<KeyValuePair<string, object?>> emittedTags)
    {
        ArgumentNullException.ThrowIfNull(emittedNames);
        ArgumentNullException.ThrowIfNull(emittedTags);

        string[] namesCarryingAPoisonedInput =
        [
            .. emittedNames
                .Where(CarriesAnySentinel)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        string[] dimensionsCarryingSomethingTheyMayNot =
        [
            .. emittedTags
                .Where(tag => CarriesAForbiddenSentinel(tag.Value)
                    || (CarriesSentinel(tag.Value, ConfiguredAliasSentinel)
                        && !DimensionsCarryingAConfiguredAlias.Contains(tag.Key, StringComparer.Ordinal)))
                .Select(tag => $"{tag.Key}={tag.Value}")
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Empty(namesCarryingAPoisonedInput);
        Assert.Empty(dimensionsCarryingSomethingTheyMayNot);
    }

    /// <summary>Asserts that every span name and dimension key an assembly declares obeys the contract.</summary>
    /// <param name="publishingAssembly">The production assembly to inspect.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="publishingAssembly" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// The emitted surface proves what ran; this proves what is written down, and the two catch different things. A
    /// name declared for a signal nothing in a unit test reaches — a worker's span, a dimension only a failure path
    /// sets — is invisible to a listener and perfectly visible here, which is why both halves exist rather than the
    /// cheaper one.
    /// </para>
    /// <para>
    /// It reads the constants by their field names rather than by their values, because that is the distinction a value
    /// cannot carry: <c>mailfathom.email-chunk.v1</c> is a hash domain and <c>mailfathom.admin</c> is a certificate
    /// store key, and neither is a dimension however much it looks like one. A field ending in <c>TagName</c> is a
    /// dimension and one ending in <c>SpanName</c> is a span, which is the convention every publisher in this
    /// repository already writes and the one the discovery check above also depends on.
    /// </para>
    /// </remarks>
    internal static void AssertEveryDeclaredNameObeysTheContract(Assembly publishingAssembly)
    {
        ArgumentNullException.ThrowIfNull(publishingAssembly);

        var declared = DeclaredTelemetryNamesIn(publishingAssembly);

        string[] offending =
        [
            .. declared
                .Where(name => !(name.IsSpan ? SpanNameShape : DimensionShape).IsMatch(name.Value)
                    || SegmentsOf(name.Value).Intersect(WordsNoSignalIsNamedAfter, StringComparer.Ordinal).Any())
                .Select(name => name.Value)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Empty(offending);
    }

    /// <summary>Reads every span name and dimension key an assembly declares as a constant.</summary>
    /// <param name="publishingAssembly">The production assembly to inspect.</param>
    /// <returns>Each declared name, and whether it names a span rather than a dimension.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="publishingAssembly" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Published beside the assertion so a suite can establish that the reader found anything at all. An assertion that
    /// no declared name is wrong passes just as quietly over an assembly whose names this failed to read.
    /// </remarks>
    internal static IReadOnlyList<(string Value, bool IsSpan)> DeclaredTelemetryNamesIn(Assembly publishingAssembly)
    {
        ArgumentNullException.ThrowIfNull(publishingAssembly);

        return
        [
            .. publishingAssembly.GetTypes()
                .SelectMany(type => type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Where(field => field.IsLiteral
                    && field.FieldType == typeof(string)
                    && (field.Name.EndsWith("SpanName", StringComparison.Ordinal)
                        || field.Name.EndsWith("TagName", StringComparison.Ordinal)))
                .Select(field => (
                    Value: field.GetRawConstantValue() as string,
                    IsSpan: field.Name.EndsWith("SpanName", StringComparison.Ordinal)))
                .Where(name => name.Value is not null)
                .Select(name => (name.Value!, name.IsSpan)),
        ];
    }

    /// <summary>Asserts that every type in the assembly that publishes a signal was driven by the suite.</summary>
    /// <param name="publishingAssembly">The production assembly to inspect.</param>
    /// <param name="drivenTypes">The publishers the suite constructed and exercised.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Without this the contract holds over whatever the suite remembered to drive, which is the failure the whole
    /// exercise exists to prevent: a publisher added next year is exactly the one nobody thought to add here.
    /// </para>
    /// <para>
    /// A publisher is found by what it holds rather than by where it lives, because a namespace is a convention a new
    /// type can be outside of without anybody noticing. Holding an <see cref="Instrument" /> is what it is to own a
    /// metric, and declaring a constant whose name ends in <c>SpanName</c> is what it is to own a span — the second is
    /// a convention too, but it is one the suite fails on rather than one it silently depends on, because a span whose
    /// name is not declared that way is a span this reports as undriven.
    /// </para>
    /// </remarks>
    internal static void AssertEveryPublisherInTheAssemblyIsDriven(
        Assembly publishingAssembly,
        IReadOnlyCollection<Type> drivenTypes)
    {
        ArgumentNullException.ThrowIfNull(publishingAssembly);
        ArgumentNullException.ThrowIfNull(drivenTypes);

        string[] undriven =
        [
            .. publishingAssembly.GetTypes()
                .Where(PublishesASignal)
                .Where(type => !drivenTypes.Contains(type))
                .Select(type => type.FullName ?? type.Name)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Empty(undriven);
    }

    private static bool PublishesASignal(Type type) =>
        type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Any(field => typeof(Instrument).IsAssignableFrom(field.FieldType)
                || (field.IsLiteral && field.Name.EndsWith("SpanName", StringComparison.Ordinal)));

    private static string[] SegmentsOf(string name) =>
        name.Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries);

    private static bool CarriesAnySentinel(string name) =>
        CarriesSentinel(name, ConfiguredAliasSentinel) || CarriesAForbiddenSentinel(name);

    private static bool CarriesAForbiddenSentinel(object? value) =>
        CarriesSentinel(value, CallerSuppliedSentinel) || CarriesSentinel(value, MailDerivedSentinel);

    private static bool CarriesSentinel(object? value, string sentinel) =>
        value?.ToString()?.Contains(sentinel, StringComparison.OrdinalIgnoreCase) == true;
}
