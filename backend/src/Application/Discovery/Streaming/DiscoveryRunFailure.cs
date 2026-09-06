// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.Discovery.Streaming;

/// <summary>Why a run stopped without finishing, in the terms a client can act on.</summary>
/// <remarks>
/// A closed set rather than a message, because what ends a run is read by a screen deciding what to offer next — retry
/// now, retry later, or say the deployment does not do this — and because an exception's own text is written for an
/// operator and may name a folder, a filter, or an account. Nothing derived from the question or from the mail reaches
/// a client through this.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<DiscoveryRunFailure>))]
public enum DiscoveryRunFailure
{
    /// <summary>This deployment answers no questions about mail, so no retry will change the outcome.</summary>
    Unavailable = 0,

    /// <summary>This deployment answers questions and currently cannot, so the same question is worth asking again.</summary>
    TemporarilyUnavailable = 1,

    /// <summary>Every lookup the plan held carried a filter this deployment refuses, so there was nothing to answer from.</summary>
    RetrievalRefused = 2,

    /// <summary>The run reached the longest a run may take and was stopped where it stood.</summary>
    TimedOut = 3,

    /// <summary>The run ended for a reason it does not publish, which an operator reads in the deployment's own logs.</summary>
    Failed = 4,

    /// <summary>
    /// The deployment stopped while the run was executing, so nothing was established about the question and asking it
    /// again is worth doing once the deployment is back.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="TimedOut" /> because the two are the same cancellation to the code that observes it
    /// and opposite facts to a person reading it: a timeout says this question was more than one run could answer, and
    /// this says the run never got its turn.
    /// </remarks>
    Stopped = 5,
}
