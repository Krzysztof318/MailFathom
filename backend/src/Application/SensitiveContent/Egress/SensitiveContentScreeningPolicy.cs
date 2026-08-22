// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent.Detection;

namespace MailFathom.Application.SensitiveContent.Egress;

/// <summary>Which of a deployment's findings stop an act rather than being redacted out of a result.</summary>
/// <remarks>
/// <para>
/// The two questions this feature answers are separate and this type is the second of them. What a deployment
/// <em>detects</em> is the scanners' switches and their category lists, and it is one answer shared by every path.
/// What a detection <em>does</em> depends on where the text was going: on the paths a reader is served from, a finding
/// is replaced by a placeholder and the reader is served; on an outgoing message it stops the act, because rewriting
/// what somebody wrote and sending it under their own address is the one disposition no result justifies.
/// </para>
/// <para>
/// Which scanner may stop an act is therefore configured rather than assumed, and the reason is the asymmetry
/// <see cref="SensitiveContentScannerKind" /> already records: a credential in a message somebody is sending is never
/// something they meant to put there, while the names, addresses, and signature blocks a personal-data scanner reports
/// are most of what ordinary correspondence is made of. A deployment that let the second stop a send would have turned
/// sending off.
/// </para>
/// <para>
/// The categories are resolved from the plan once rather than read per finding, so a category a scanner is not looking
/// for cannot appear here and a scanner that is switched off contributes none. A policy that refuses nothing is a
/// policy that exists — the composition still holds it — and <see cref="RefusesAnything" /> is what a consumer reads to
/// skip work that only a screen makes necessary.
/// </para>
/// </remarks>
public sealed class SensitiveContentScreeningPolicy
{
    private readonly IReadOnlyDictionary<string, SensitiveContentScannerKind> screenedCategories;

    private SensitiveContentScreeningPolicy(
        IReadOnlyDictionary<string, SensitiveContentScannerKind> screenedCategories) =>
        this.screenedCategories = screenedCategories;

    /// <summary>Gets whether any finding this deployment can produce stops a screened act.</summary>
    public bool RefusesAnything => this.screenedCategories.Count > 0;

    /// <summary>Composes the policy of a deployment from its plan and the scanners it lets stop an act.</summary>
    /// <param name="plan">What this deployment scans for.</param>
    /// <param name="screeningScanners">The scanners whose findings stop a screened act, in any order and possibly none.</param>
    /// <returns>The policy every screened act is judged by.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// A named scanner that is switched off contributes nothing rather than failing, for the reason the plan itself is
    /// composed that way: the switch decides whether a scanner runs at all, and a deployment that turned one off has
    /// said what it wants of every path that scanner would have reached.
    /// </remarks>
    public static SensitiveContentScreeningPolicy Create(
        SensitiveContentPlan plan,
        IReadOnlyList<SensitiveContentScannerKind> screeningScanners)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(screeningScanners);

        var screened = screeningScanners
            .Distinct()
            .Order()
            .Select(scanner => plan.TryGetScanner(scanner, out var scannerPlan) ? scannerPlan : null)
            .Where(scannerPlan => scannerPlan is not null)
            .SelectMany(scannerPlan => scannerPlan!.Categories.Select(
                category => (category.Name, scannerPlan.Scanner)))
            // Grouped rather than collected straight into a dictionary, because nothing stops two catalogs from
            // declaring a category of the same name. The scanner kept is the lower one, which the ordering above makes
            // reproducible rather than a matter of which catalog was registered first.
            .GroupBy(screening => screening.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                sameName => sameName.Key,
                sameName => sameName.First().Scanner,
                StringComparer.OrdinalIgnoreCase);

        return new SensitiveContentScreeningPolicy(screened);
    }

    /// <summary>Composes the policy of a deployment that screens nothing, which is every deployment that scans nothing.</summary>
    /// <returns>A policy no finding reaches.</returns>
    public static SensitiveContentScreeningPolicy ScreeningNothing() =>
        new(new Dictionary<string, SensitiveContentScannerKind>(StringComparer.OrdinalIgnoreCase));

    /// <summary>Finds which scanner's category stops the act one finding was found in, if any does.</summary>
    /// <param name="finding">The finding a scan produced.</param>
    /// <returns>The scanner whose category matched, or <see langword="null" /> where this finding stops nothing.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="finding" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The scanner is answered from the plan rather than from the finding, because a finding names its detector and its
    /// rule and never which switch the deployment turned on to get it. The category is matched case-insensitively for
    /// the reason every category name is compared that way here: the catalog's spelling is authoritative and an
    /// operator's is not.
    /// </remarks>
    public SensitiveContentScannerKind? StoppedBy(SensitiveContentFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        return this.screenedCategories.TryGetValue(finding.Category.Name, out var scanner) ? scanner : null;
    }
}
