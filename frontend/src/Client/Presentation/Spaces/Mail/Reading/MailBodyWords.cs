// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Mail;
using Microsoft.Extensions.Localization;

namespace MailFathom.Client.Presentation.Spaces.Mail.Reading;

/// <summary>The sentences the reading pane composes rather than authors against a control.</summary>
/// <param name="UnsupportedBlock">What stands where a part of the message is one this build cannot draw.</param>
/// <param name="UndrawnImage">What a picture the message carried but nothing drew is described as.</param>
/// <param name="LinkTitle">What the question asked before a link is followed is headed with.</param>
/// <param name="LinkDisplayText">What the words the message put on the link are labelled with.</param>
/// <param name="LinkTarget">What the address the link actually goes to is labelled with.</param>
/// <param name="LinkPunycode">What the second spelling of a host written in another script is labelled with.</param>
/// <param name="LinkDeception">What is said where the words on the link name a different host from the one it goes to.</param>
/// <param name="LinkHomograph">What is said where the host is written in two spellings and nothing said the text lies.</param>
/// <param name="LinkUnjudged">What is said where the deployment reported a verdict this build cannot read.</param>
/// <param name="LinkOpen">What the answer that follows the link is called.</param>
/// <param name="LinkCancel">What the answer that does not is called.</param>
/// <remarks>
/// Carried as a value rather than resolved where it is needed, because what needs it is a view: reaching a localizer
/// from one would put a service in the visual tree, and the model already holds the localizer the rest of this space
/// reads. The unit suite holds every name below to answering in each language.
/// </remarks>
public sealed record MailBodyWords(
    string UnsupportedBlock,
    string UndrawnImage,
    string LinkTitle,
    string LinkDisplayText,
    string LinkTarget,
    string LinkPunycode,
    string LinkDeception,
    string LinkHomograph,
    string LinkUnjudged,
    string LinkOpen,
    string LinkCancel)
{
    /// <summary>The entry each sentence above is authored under, in the order the record states them.</summary>
    /// <remarks>
    /// One list rather than a name beside each property, because what a test has to hold is that every entry answers,
    /// and a name written twice is how one of them stops being checked.
    /// </remarks>
    public static IReadOnlyList<string> ResourceKeys { get; } =
    [
        "MailBody.Block.Unsupported",
        "MailBody.Image.Undrawn",
        "MailBody.Link.Title",
        "MailBody.Link.DisplayText",
        "MailBody.Link.Target",
        "MailBody.Link.Punycode",
        "MailBody.Link.Deception",
        "MailBody.Link.Homograph",
        "MailBody.Link.Unjudged",
        "MailBody.Link.Open",
        "MailBody.Link.Cancel",
    ];

    /// <summary>Reads every sentence in the language the person is reading in.</summary>
    /// <param name="words">Where the sentences come from.</param>
    /// <returns>The sentences, resolved once for the body about to be drawn.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="words" /> is <see langword="null" />.</exception>
    public static MailBodyWords From(IStringLocalizer words)
    {
        ArgumentNullException.ThrowIfNull(words);

        return new MailBodyWords(
            words[ResourceKeys[0]],
            words[ResourceKeys[1]],
            words[ResourceKeys[2]],
            words[ResourceKeys[3]],
            words[ResourceKeys[4]],
            words[ResourceKeys[5]],
            words[ResourceKeys[6]],
            words[ResourceKeys[7]],
            words[ResourceKeys[8]],
            words[ResourceKeys[9]],
            words[ResourceKeys[10]]);
    }

    /// <summary>Says what the deployment actually found about a link, out of the three grounds a warning opens on.</summary>
    /// <param name="link">The link as the deployment judged it.</param>
    /// <returns>The sentence naming what was found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="link" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The verdict is read off the contract rather than re-derived, and it picks the sentence rather than merely
    /// opening the bar: a reader told the words name a different site, who then checks the two spellings against the
    /// target and finds they agree, learns to distrust the bar — which is the one thing a warning cannot afford. A
    /// host written in two spellings is its own finding and says so, and a verdict this build cannot read says that
    /// instead of naming a mismatch nobody established.
    /// </remarks>
    public string WarningAbout(MailBodyLink link)
    {
        ArgumentNullException.ThrowIfNull(link);

        return link.Deception switch
        {
            MailBodyLinkDeception.DisplayedHostDiffers => this.LinkDeception,
            MailBodyLinkDeception.Unrecognized => this.LinkUnjudged,
            _ => this.LinkHomograph,
        };
    }

    /// <summary>Names the entry the reason a message is read as words rather than as a document is authored under.</summary>
    /// <param name="refusal">The reason the deployment gave.</param>
    /// <returns>The resource key.</returns>
    /// <remarks>
    /// Composed from the value rather than chosen by a switch, so a reason added to the contract is a missing string
    /// the resource-table test names rather than a body that silently falls back to no reason at all.
    /// </remarks>
    public static string RefusalResourceKeyFor(MailBodyRefusal refusal) => $"MailBody.Refusal.{refusal}";
}
