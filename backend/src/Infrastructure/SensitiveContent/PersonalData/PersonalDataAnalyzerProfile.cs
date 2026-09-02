// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.RegularExpressions;
using MailFathom.Application.SensitiveContent.Detection;

namespace MailFathom.Infrastructure.SensitiveContent.PersonalData;

/// <summary>Where the personal-data analyzer is, which languages it is asked in, and what a finding it produced is attributed to.</summary>
/// <remarks>
/// <para>
/// The analyzer is <b>expected to be deployment-local</b>. The whole point of scanning is that content is inspected before
/// it leaves the trust boundary, so an address on the public internet defeats the feature: the mail is then handed to a
/// third party in order to find out whether it may be handed to a third party. Nothing here prevents it, because a
/// deployment may legitimately run one analyzer for several services inside its own network, and no rule about addresses
/// could tell the two cases apart.
/// </para>
/// <para>
/// The languages belong here rather than beside the categories because they are a property of the analyzer's own
/// configuration: a model is loaded per language and a recognizer registered per language, so asking in one the analyzer
/// has none for is refused rather than answered. They are part of the detector revision for the same reason the mapping
/// is — the same text asked in two sets of languages is two results, and a consumer that stored one has to be able to
/// say which.
/// </para>
/// <para>
/// The confidence floor is part of that revision too, and the line between what belongs in it and what does not is
/// which side of the boundary applies the value. The floor travels on the request and the analyzer enforces it, so a
/// finding below it never crosses the boundary and two deployments differing only there were not asked the same
/// question. The categories and the suppressions are what stay out: the plan applies those here, over an answer both
/// deployments received in full.
/// </para>
/// </remarks>
public sealed partial record PersonalDataAnalyzerProfile
{
    /// <summary>The name of the outbound client every call to the analyzer is made through.</summary>
    /// <remarks>
    /// Declared on this type rather than on either of its two consumers, because the scanner and the startup probe reach
    /// one analyzer under one set of bounds and a name on one of them would leave the other reading a string it does not
    /// own. <c>CreateClient</c> with an unregistered name answers with an unbounded client rather than failing, so the
    /// agreement between the registration and both call sites has to be a compile-time one.
    /// </remarks>
    public const string TransportName = "personal-data-analyzer";

    /// <summary>The greatest number of languages one deployment may be configured to ask in.</summary>
    /// <remarks>
    /// Two bounds meet at roughly the same number and this is the lower of them. A scan asks once per language inside the
    /// one budget <c>SensitiveContent:ScanTimeout</c> allows, so each language is another request the same deadline has to
    /// cover; and the revision below names every one of them inside the 64 characters
    /// <see cref="SensitiveContentDetector" /> accepts, which a longer list overflows into a startup failure naming a
    /// grammar rather than the key an operator wrote.
    /// </remarks>
    public const int MaximumLanguages = 8;

    private PersonalDataAnalyzerProfile(
        Uri endpoint,
        IReadOnlyList<string> languages,
        double minimumConfidence,
        SensitiveContentDetector detector)
    {
        this.Endpoint = endpoint;
        this.Languages = languages;
        this.MinimumConfidence = minimumConfidence;
        this.Detector = detector;
    }

    /// <summary>Gets the analyzer's base address.</summary>
    public Uri Endpoint { get; }

    /// <summary>Gets the languages the analyzer is asked in, each a two-letter code, deduplicated and ordered.</summary>
    /// <remarks>
    /// A set rather than a sequence, whatever an operator wrote. The order carries no precedence — an overlap between two
    /// languages' findings is settled by the redactor's own ordering rule rather than by which language reported first —
    /// so normalizing here is what makes two deployments configured with the same languages produce the same revision and
    /// the same text.
    /// </remarks>
    public IReadOnlyList<string> Languages { get; }

    /// <summary>Gets how sure the analyzer must be before a finding is reported, from 0 to 1.</summary>
    /// <remarks>
    /// Stated on the request so the analyzer applies it, rather than applied to the answer here. It is the analyzer that
    /// knows what a score means for each of its recognizers, and a floor it enforces itself is one that keeps the weakest
    /// guesses off the wire instead of carrying them across a process boundary in order to discard them.
    /// </remarks>
    public double MinimumConfidence { get; }

    /// <summary>Gets the identity and revision every finding this scanner produces carries.</summary>
    /// <remarks>
    /// The revision names the mapping this build ships, the set of languages the text was asked about, and the floor the
    /// request states. The floor is in it because the analyzer applies it rather than this process: a finding below it never
    /// crosses the boundary, so two deployments differing only there were not asked the same question and their answers
    /// are not comparable. That matters beyond a finding — a derived row's stamp is computed from this revision, so a
    /// lowered floor is what tells an already-indexed mailbox it was redacted under a weaker one.
    /// </remarks>
    public SensitiveContentDetector Detector { get; }

    /// <summary>Composes the profile a configured analyzer is reached under.</summary>
    /// <param name="endpoint">The analyzer's base address.</param>
    /// <param name="languages">The two-letter codes of the languages it is asked in, in any order and with repeats allowed.</param>
    /// <param name="minimumConfidence">How sure the analyzer must be before it reports a finding, from 0 to 1.</param>
    /// <returns>The validated profile, carrying the languages deduplicated and ordered.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoint" /> or <paramref name="languages" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the address is not an absolute HTTP address, when no language is named, when one of them is not a two-letter code, or when more than <see cref="MaximumLanguages" /> are named.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="minimumConfidence" /> is not a number between 0 and 1 inclusive.</exception>
    /// <remarks>
    /// A relative address is refused rather than resolved against something, because there is nothing to resolve it
    /// against: a base address is what every call to the analyzer is composed from. A scheme other than HTTP is refused
    /// for the same reason a mistyped port would be — the request would never arrive, and startup is where that is worth
    /// finding out. An empty set of languages is refused for a sharper reason: a scan states a language on every request,
    /// so a deployment asking in none would scan every switched-on category and find nothing in it.
    /// </remarks>
    public static PersonalDataAnalyzerProfile Create(
        Uri endpoint,
        IEnumerable<string> languages,
        double minimumConfidence)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(languages);

        if (!endpoint.IsAbsoluteUri || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            // The address it was given is deliberately not echoed, as in the options validator this normally runs behind:
            // a message reaches a log and a host name never does.
            throw new ArgumentException(
                "That is not an address the personal-data analyzer can be reached at. State an absolute http or https address, such as http://presidio-analyzer:3000.",
                nameof(endpoint));
        }

        var asked = Normalize(languages);

        if (double.IsNaN(minimumConfidence) || minimumConfidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumConfidence),
                minimumConfidence,
                "The analyzer's confidence floor is a share of certainty, so it lies between 0 and 1 inclusive.");
        }

        return new PersonalDataAnalyzerProfile(
            WithTrailingSlash(endpoint),
            asked,
            minimumConfidence,
            SensitiveContentDetector.Create(
                PresidioEntityCorpus.DetectorName,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "presidio+entities.{0}+lang.{1}+floor.{2:0.###}",
                    PresidioEntityCorpus.MappingRevision,
                    string.Join('.', asked),
                    minimumConfidence)));
    }

    /// <inheritdoc />
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "{0} in {1}",
        this.Endpoint,
        string.Join(", ", this.Languages));

    /// <summary>Turns whatever an operator wrote into the set the analyzer is asked in.</summary>
    /// <remarks>
    /// Deduplicated and ordered rather than taken as given, so the revision every finding carries is a property of which
    /// languages were configured rather than of the order they were typed in. Two deployments that named the same
    /// languages are then comparable, which is the whole of what a stamp is for.
    /// </remarks>
    private static string[] Normalize(IEnumerable<string> languages)
    {
        var stated = languages.ToArray();

        // Before the set is formed rather than after, because StringComparer.Ordinal refuses to hash a null and the
        // deduplication would raise that instead of the message an operator can act on.
        var malformed = Array.FindIndex(stated, language => language is null || !AcceptedLanguage.IsMatch(language));

        if (malformed >= 0)
        {
            throw new ArgumentException(
                $"'{stated[malformed]}' is not an acceptable analyzer language. State the two-letter lowercase code of a language the analyzer's own configuration loads a model for, such as en.",
                nameof(languages));
        }

        var asked = stated.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        if (asked.Length == 0)
        {
            throw new ArgumentException(
                "The personal-data analyzer is asked in at least one language, and none was named. State the two-letter lowercase code of every language the analyzer's own configuration loads a model for, such as en.",
                nameof(languages));
        }

        if (asked.Length > MaximumLanguages)
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The personal-data analyzer is asked in at most {0} languages and {1} were named. Each one is another request inside the same per-scan budget, and the revision every finding carries names them all.",
                    MaximumLanguages,
                    asked.Length),
                nameof(languages));
        }

        return asked;
    }

    /// <summary>Ensures the address is usable as a base address rather than as one path of an analyzer.</summary>
    /// <remarks>
    /// A base address whose path does not end in a slash loses its last segment when a route is resolved against it, so
    /// an analyzer served behind a reverse proxy at <c>https://gateway/presidio</c> would be asked at
    /// <c>https://gateway/analyze</c> — a request that arrives somewhere real and answers something else. Normalizing here
    /// is what lets an operator write the address the way they think of it.
    /// </remarks>
    private static Uri WithTrailingSlash(Uri endpoint) => endpoint.AbsolutePath.EndsWith('/')
        ? endpoint
        : new Uri($"{endpoint.GetLeftPart(UriPartial.Path)}/", UriKind.Absolute);

    [GeneratedRegex(@"\A[a-z]{2}\z", RegexOptions.CultureInvariant)]
    private static partial Regex AcceptedLanguage { get; }
}
