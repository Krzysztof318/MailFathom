// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Generation;

/// <summary>Names the subject matter an AI-generated message is written about.</summary>
/// <remarks>
/// <para>
/// A closed enumeration rather than a C# <see langword="enum" />, because a topic is inseparable from the
/// description the generation prompt hands to the model: the terminology, vocabulary, and context a message about the
/// topic should carry travel with the value. Keeping that in a separate lookup table would let the two drift apart,
/// and a new topic is one member with its own description rather than a name in one place and a string in another.
/// </para>
/// <para>
/// The set is the initial one the tool ships, not a limit on what the tool can generate: adding a topic is adding a
/// member, which is what the closed shape keeps a one-line change. The value crosses only the command-line boundary —
/// nothing serializes it — so it carries no JSON converter, unlike the closed enumerations that cross a file.
/// </para>
/// <para>
/// Being a struct, <see langword="default" /> is reachable and is not a topic. The command line refuses it before a
/// plan is built, and the generator refuses a plan whose distribution names one.
/// </para>
/// </remarks>
internal readonly record struct SyntheticMailTopic
{
    private readonly string? name;
    private readonly string? promptDescription;

    private SyntheticMailTopic(string name, string promptDescription)
    {
        this.name = name;
        this.promptDescription = promptDescription;
    }

    /// <summary>Gets the topic of ordinary business correspondence: scheduling, follow-ups, and coordination between people.</summary>
    public static SyntheticMailTopic Business { get; } = new(
        "business",
        "ordinary business correspondence: scheduling and confirming meetings, following up on decisions, coordinating between colleagues, and agreeing next steps");

    /// <summary>Gets the topic of invoices, payments, and billing questions.</summary>
    public static SyntheticMailTopic Invoices { get; } = new(
        "invoices",
        "invoices, payments, and billing: confirming an invoice was received, asking about an outstanding payment, correcting a billing detail, and sending a payment confirmation");

    /// <summary>Gets the topic of technical support: a problem, its reproduction, and its resolution.</summary>
    public static SyntheticMailTopic TechnicalSupport { get; } = new(
        "technical-support",
        "technical support: reporting a problem with software or a service, asking how to reproduce or fix it, sending a workaround or a resolution, and following up until it is closed");

    /// <summary>Gets the topic of travel: booking, planning, and arranging a trip.</summary>
    public static SyntheticMailTopic Travel { get; } = new(
        "travel",
        "travel: booking and confirming flights, hotels, or car rentals, planning an itinerary, arranging pickups and accommodation, and dealing with a change to a booked trip");

    /// <summary>Gets every supported topic.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<SyntheticMailTopic> All { get; } =
    [
        Business,
        Invoices,
        TechnicalSupport,
        Travel,
    ];

    /// <summary>Gets whether this value names a supported topic rather than the unusable struct default.</summary>
    public bool IsSpecified => this.name is not null;

    /// <summary>Gets the name the command line accepts and a refusal reports.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a topic.</exception>
    public string Name => this.name
        ?? throw new InvalidOperationException("The value is the default of the struct and does not name a topic.");

    /// <summary>Gets the subject matter the generation prompt hands to the model.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a topic.</exception>
    public string PromptDescription => this.promptDescription
        ?? throw new InvalidOperationException("The value is the default of the struct and does not name a topic.");

    /// <summary>Parses a command-line topic name, ignoring case and surrounding whitespace.</summary>
    /// <param name="name">The topic name as written after <c>--topic</c>.</param>
    /// <param name="topic">The parsed topic when the name is supported; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the name is a supported topic; otherwise <see langword="false" />.</returns>
    public static bool TryParse(string? name, out SyntheticMailTopic topic)
    {
        topic = default;

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        // No supported topic is the struct default, so an unmatched name yields the unspecified value the caller
        // already receives when parsing fails.
        topic = All.FirstOrDefault(candidate => StringComparer.OrdinalIgnoreCase.Equals(candidate.Name, name.Trim()));

        return topic.IsSpecified;
    }

    /// <inheritdoc />
    public override string ToString() => this.name ?? "(unspecified)";
}
