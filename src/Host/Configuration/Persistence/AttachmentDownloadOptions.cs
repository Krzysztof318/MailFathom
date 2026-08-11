// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace MailFathom.Host.Configuration.Persistence;

/// <summary>Configures how long the short-lived links a read hands back stay redeemable.</summary>
/// <remarks>
/// <para>
/// One setting, because the other half of a link — where it points — is a fact about the deployment rather than about
/// attachments, and lives in <see cref="DeploymentOptions.PublicBaseAddress" />. A deployment that declares no address
/// issues no link, whatever this block says.
/// </para>
/// <para>
/// A link is a bearer capability: it names one attachment of one email, it carries a signature, and it needs no
/// credential to redeem. That is what makes the window the control — a leaked URL is worth something only until it
/// expires — and it is why the ceiling here is the product's rather than the operator's.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class AttachmentDownloadOptions
{
    /// <summary>The configuration path this block is bound from, used to name a faulty setting.</summary>
    internal const string SectionPath = "EmailContent:AttachmentDownloads";

    /// <summary>The shortest lifetime a deployment may configure.</summary>
    /// <remarks>
    /// A link is useless before whatever fetches it has been handed the URL, and that hand-off crosses a protocol
    /// response, a client, and often a separate process. A minute is the smallest window in which that reliably
    /// completes; anything shorter would produce links that expire between being issued and being read.
    /// </remarks>
    internal static readonly TimeSpan MinimumLifetime = TimeSpan.FromMinutes(1);

    /// <summary>The longest lifetime a deployment may configure.</summary>
    /// <remarks>
    /// A URL is copied into proxy logs, browser history, and chat transcripts by software nobody here controls, so what
    /// bounds the damage of one leaking is how soon it dies. Half an hour keeps every issued link inside the minutes
    /// this capability is designed around; beyond that it stops being a capability and becomes a credential the
    /// deployment cannot revoke.
    /// </remarks>
    internal static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(30);

    /// <summary>Gets or sets how long a minted link stays redeemable.</summary>
    /// <remarks>Ten minutes unless a deployment says otherwise: long enough for a client to hand the URL to whatever fetches it, short enough that a leaked link is usually already dead.</remarks>
    public TimeSpan LinkLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Finds everything an operator must fix before links can be issued.</summary>
    /// <returns>One message per faulty setting, each naming its configuration path, empty when the settings are usable.</returns>
    /// <remarks>A value out of range is refused rather than clamped, the way every other unusable option here is.</remarks>
    public IReadOnlyList<string> FindConfigurationErrors()
    {
        if (this.LinkLifetime >= MinimumLifetime && this.LinkLifetime <= MaximumLifetime)
        {
            return [];
        }

        return
        [
            string.Format(
                CultureInfo.InvariantCulture,
                "{0}:{1} is '{2}', which is outside the permitted range of {3} to {4}. A shorter window would expire links before a client could fetch them, and a longer one would turn a capability nobody can revoke into a durable credential.",
                SectionPath,
                nameof(this.LinkLifetime),
                this.LinkLifetime,
                MinimumLifetime,
                MaximumLifetime),
        ];
    }
}
