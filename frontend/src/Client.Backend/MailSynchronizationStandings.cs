// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend;

/// <summary>Reads the word a deployment publishes a standing as into the standing this client knows.</summary>
/// <remarks>
/// One reader rather than one per document, because the accounts route and the folders route publish the same
/// vocabulary and a client that read them separately could come to disagree with itself about one mailbox.
/// </remarks>
public static class MailSynchronizationStandings
{
    /// <summary>Reads a published standing.</summary>
    /// <param name="published">The word the deployment sent, which may be <see langword="null" /> where the document named none.</param>
    /// <returns>The standing, or <see cref="MailSynchronizationStanding.Unrecognized" /> where this build does not know the word.</returns>
    /// <remarks>
    /// Matched against the published names exactly rather than parsed with <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)" />,
    /// which would also accept a number, a case that differs, and a comma-separated pair — none of which the contract
    /// publishes, and each of which would turn a document this client does not understand into a claim about somebody's
    /// mail.
    /// </remarks>
    public static MailSynchronizationStanding Read(string? published) => published switch
    {
        "NeverSynchronized" => MailSynchronizationStanding.NeverSynchronized,
        "Synchronized" => MailSynchronizationStanding.Synchronized,
        "Failing" => MailSynchronizationStanding.Failing,
        "Unreachable" => MailSynchronizationStanding.Unreachable,
        _ => MailSynchronizationStanding.Unrecognized,
    };
}
