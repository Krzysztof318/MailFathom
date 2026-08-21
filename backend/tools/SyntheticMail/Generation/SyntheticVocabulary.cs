// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Generation;

/// <summary>Everything a generated message is built from, carried in the repository rather than fetched or written by a model.</summary>
/// <remarks>
/// <para>
/// A model would make the tool depend on a configured provider to do its job and would write a different mailbox on
/// every run, which is exactly what the seed exists to prevent. Word lists and templates cost nothing, reach nothing,
/// and are the reason a corpus can be asserted against at all.
/// </para>
/// <para>
/// The content is deliberately recognizable as invented. Anything that read like a real thread would raise the very
/// question this tool exists to avoid, so the vocabulary is drawn from harbours and cartography rather than from
/// anything an office writes about.
/// </para>
/// </remarks>
internal static class SyntheticVocabulary
{
    /// <summary>The reserved top-level domain every fabricated address ends in.</summary>
    /// <remarks>
    /// RFC 6761 reserves <c>.test</c> for testing and development and guarantees it resolves to nothing, so a
    /// generated address cannot reach a person even if it is echoed into a reply, a forward, or a mailing list. The
    /// address given on the command line is the only real one a run ever touches.
    /// </remarks>
    internal const string ReservedTopLevelDomain = ".test";

    /// <summary>The fabricated domains participants are drawn from, all under the reserved top-level domain.</summary>
    internal static IReadOnlyList<string> Domains { get; } =
    [
        "harbourline.test",
        "quietfjord.test",
        "blueheron.test",
        "latticeworks.test",
        "northreach.test",
        "saltmarsh.test",
    ];

    /// <summary>Given names, several of which carry characters outside ASCII.</summary>
    /// <remarks>
    /// The non-ASCII entries are not decoration. A display name outside ASCII forces RFC 2047 encoded words into the
    /// header, which is a shape the MIME metadata extractor reads and a plain-ASCII corpus would never produce.
    /// </remarks>
    internal static IReadOnlyList<string> GivenNames { get; } =
    [
        "Ada", "Bram", "Cecilia", "Dorotea", "Eamon", "Frida", "Gustav", "Halina",
        "Ingrid", "Jarek", "Katharina", "Lubomír", "Maëlys", "Niamh", "Øyvind", "Petra",
        "Quentin", "Rosalía", "Søren", "Tereza", "Ulrike", "Vasco", "Wiebke", "Zofia",
    ];

    /// <summary>Family names, on the same reasoning as the given names.</summary>
    internal static IReadOnlyList<string> FamilyNames { get; } =
    [
        "Almqvist", "Bergström", "Caron", "Dvořák", "Esposito", "Falkenrath", "Gutiérrez", "Hjelm",
        "Iversen", "Jankowski", "Kowalczyk", "Lindqvist", "Mäkinen", "Nordbø", "Oliveira", "Pettersen",
        "Quesada", "Rautavaara", "Szabó", "Thorvaldsen", "Ubaldi", "Vestergaard", "Wojciechowski", "Zielinska",
    ];

    /// <summary>The nouns a subject and a body are composed from.</summary>
    internal static IReadOnlyList<string> Nouns { get; } =
    [
        "harbour", "lighthouse", "ferry", "tideline", "breakwater", "chart", "buoy", "jetty",
        "sandbank", "estuary", "channel", "beacon", "mooring", "quay", "shoal", "headland",
        "anchorage", "fairway", "lock gate", "pontoon", "slipway", "seawall", "groyne", "lagoon",
    ];

    /// <summary>The verbs a subject and a body are composed from.</summary>
    internal static IReadOnlyList<string> Verbs { get; } =
    [
        "surveys", "dredges", "repaints", "inspects", "relocates", "measures", "charts", "reinforces",
        "reopens", "closes", "widens", "lights", "marks", "clears", "sounds", "photographs",
    ];

    /// <summary>The adjectives a subject and a body are composed from.</summary>
    internal static IReadOnlyList<string> Adjectives { get; } =
    [
        "northern", "silted", "tidal", "disused", "floodlit", "sheltered", "exposed", "narrow",
        "dredged", "buoyed", "seasonal", "drifting", "granite", "timber", "concrete", "weathered",
    ];

    /// <summary>The subject templates, which vary the length of a subject as much as its wording.</summary>
    /// <remarks>Each placeholder is filled from the lists above: <c>{0}</c> an adjective, <c>{1}</c> a noun, <c>{2}</c> a verb, <c>{3}</c> a second noun.</remarks>
    internal static IReadOnlyList<string> SubjectTemplates { get; } =
    [
        "{1}",
        "{0} {1}",
        "The {0} {1}",
        "{1} report",
        "Re-survey of the {0} {1}",
        "Who {2} the {0} {1}?",
        "{1} and {3}: what the survey found",
        "The {0} {1} that {2} the {3} every spring, and what it costs to keep it there",
        "Notes on the {0} {1}, the {3}, and every reason the schedule slipped again this quarter",
    ];

    /// <summary>The sentence templates a body paragraph is composed from.</summary>
    internal static IReadOnlyList<string> SentenceTemplates { get; } =
    [
        "The {0} {1} {2} the {3} once a season.",
        "Nobody has measured the {1} since the {3} was moved.",
        "A {0} {1} costs less to keep than the {3} it replaced.",
        "The survey team {2} the {1} whenever the {3} is dry.",
        "Every {0} {1} in the estuary {2} the same {3}.",
        "We should agree what the {1} is for before the {3} arrives.",
    ];

    /// <summary>Closing lines that reach past ASCII but stay inside Latin-1.</summary>
    /// <remarks>
    /// Every character here is representable in <c>iso-8859-1</c>, which is what lets a body be encoded in that
    /// charset without an encoder silently substituting a question mark. An em dash is deliberately absent for exactly
    /// that reason.
    /// </remarks>
    internal static IReadOnlyList<string> Latin1ClosingLines { get; } =
    [
        "Grüße aus dem Hafen, die Vermessung läuft weiter.",
        "Bien cordialement, depuis l'estuaire embrumé.",
        "Med vänlig hälsning från den norra kajen.",
        "Saludos desde el faro; la revisión continúa.",
    ];

    /// <summary>Closing lines that reach past Latin-1, so a body is genuinely a UTF-8 one.</summary>
    internal static IReadOnlyList<string> UnicodeClosingLines { get; } =
    [
        "Pozdrowienia z latarni; przegląd potrwa do końca miesiąca.",
        "Χαιρετισμοί από το λιμάνι — η μέτρηση συνεχίζεται.",
        "港からのご挨拶です。測量は続いています。",
        "Zdravím z majáku — průzkum pokračuje až do jara.",
    ];

    /// <summary>The attachment file names a generated message draws from.</summary>
    internal static IReadOnlyList<string> AttachmentNames { get; } =
    [
        "tide-table.csv",
        "survey-notes.txt",
        "channel-depths.csv",
        "beacon-log.txt",
        "harbour-plan.bin",
    ];
}
