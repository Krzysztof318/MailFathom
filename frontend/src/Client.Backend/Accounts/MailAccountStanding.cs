// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Accounts;

/// <summary>Where one account's local copy stands, as the client reads the name the deployment published.</summary>
/// <remarks>
/// <para>
/// The client's own reading of a field the deployment answers with as a word, rather than a type shared across the two
/// stacks — the same arrangement <c>DeploymentRoutes</c> has with the paths beside it. What the two ends agree on is
/// the three names on the wire; what this adds is the fourth member, which no deployment ever sends.
/// </para>
/// <para>
/// <see cref="Unrecognized" /> is what a name this version of the client does not know is read as, and it is the
/// safest of the four to fall into: the major version is <c>0</c> and the service may publish a fourth standing
/// before a client is upgraded to understand it. Reading such a name as <see cref="Synchronized" /> would tell
/// somebody their mail is current on the strength of a word this build cannot interpret.
/// </para>
/// </remarks>
public enum MailAccountStanding
{
    /// <summary>The deployment named a standing this client does not know, so nothing is claimed about the copy.</summary>
    Unrecognized = 0,

    /// <summary>No run has ever durably committed progress for the account.</summary>
    NeverSynchronized = 1,

    /// <summary>Progress has been committed and the deployment's most recent finished run of the account did not fail.</summary>
    Synchronized = 2,

    /// <summary>The deployment's most recent finished run of the account did not complete, whether or not it has ever synchronized.</summary>
    Failing = 3,
}
