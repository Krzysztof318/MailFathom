// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend;

/// <summary>Where one local copy stands, as the client reads the name the deployment published.</summary>
/// <remarks>
/// <para>
/// The client's own reading of a field the deployment answers with as a word, rather than a type shared across the two
/// stacks — the same arrangement <see cref="DeploymentRoutes" /> has with the paths beside it. It sits beside those
/// paths rather than under either route's own folder because both routes publish this same vocabulary: an account's
/// standing is the reduction of its folders', so a tree and the mailbox list beside it would disagree the moment the
/// two ends were read as two different sets of words.
/// </para>
/// <para>
/// <see cref="Unrecognized" /> is what a name this version of the client does not know is read as, and it is the
/// safest of the five to fall into: the major version is <c>0</c> and the service may publish a further standing
/// before a client is upgraded to understand it. Reading such a name as <see cref="Synchronized" /> would tell
/// somebody their mail is current on the strength of a word this build cannot interpret.
/// </para>
/// </remarks>
public enum MailSynchronizationStanding
{
    /// <summary>The deployment named a standing this client does not know, so nothing is claimed about the copy.</summary>
    Unrecognized = 0,

    /// <summary>No run has ever durably committed progress.</summary>
    NeverSynchronized = 1,

    /// <summary>Progress has been committed and the deployment's most recent finished run did not fail.</summary>
    Synchronized = 2,

    /// <summary>The deployment's most recent finished run did not complete, whether or not it has ever synchronized.</summary>
    Failing = 3,

    /// <summary>The mail server did not serve the deployment within its resilience budget, so nothing is refreshing the copy.</summary>
    /// <remarks>
    /// Kept apart from <see cref="Failing" /> because the two ask different things of whoever reads them: an
    /// unreachable mailbox is waited out or looked at on the server, and a failing one is a mapping, a credential, or a
    /// defect. A run that also went wrong some other way reached the server and is reported as failing instead.
    /// </remarks>
    Unreachable = 4,
}
