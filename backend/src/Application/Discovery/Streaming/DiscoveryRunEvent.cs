// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.Discovery.Streaming;

/// <summary>One thing that happened during a Discover run, named against the run and placed in its order.</summary>
/// <remarks>
/// <para>
/// The contract a client renders a run from. It is closed by a private protected constructor, so the six events
/// declared beside it are the whole of it and a client that handles those six handles every run this build produces.
/// </para>
/// <para>
/// <strong>Every event names its run and its place in it.</strong> The sequence is assigned where the event is
/// published rather than by whatever composed it, starts at one, and increases by one — so a client renders in arrival
/// order without sorting, and a client that reconnects asks for everything after the last sequence it saw. That is the
/// whole of the resumption contract: no event is republished with a different number and none is skipped.
/// </para>
/// <para>
/// <strong>The stream is the plan, delivered as it becomes ready.</strong> The start says which revision of the
/// presentation contract the run writes; each source is declared before the block that names it; and the ending says
/// what the run knows about its own reach. A client assembling those has what a whole
/// <see cref="Presentation.PresentationPlan" /> would have carried, and a run that failed after two blocks leaves those
/// two on the screen rather than nothing.
/// </para>
/// <para>
/// A block and a citation are composed from somebody's correspondence and are sensitive throughout. They belong in the
/// response that streams them and nowhere else — never in a log line, a span attribute, or an exception message. The
/// four other events carry counts and closed values alone, which is what makes a run observable without any of it.
/// </para>
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "event")]
[JsonDerivedType(typeof(DiscoveryRunStarted), DiscoveryRunStarted.Kind)]
[JsonDerivedType(typeof(DiscoveryRetrievalProgressed), DiscoveryRetrievalProgressed.Kind)]
[JsonDerivedType(typeof(DiscoveryCitationDeclared), DiscoveryCitationDeclared.Kind)]
[JsonDerivedType(typeof(DiscoveryBlockComposed), DiscoveryBlockComposed.Kind)]
[JsonDerivedType(typeof(DiscoveryRunCompleted), DiscoveryRunCompleted.Kind)]
[JsonDerivedType(typeof(DiscoveryRunFailed), DiscoveryRunFailed.Kind)]
public abstract record DiscoveryRunEvent
{
    private protected DiscoveryRunEvent()
    {
    }

    /// <summary>Gets the run this happened in.</summary>
    /// <remarks>
    /// Carried on the event rather than left to the connection it arrived over, so a client holding two runs never has
    /// to infer which one an event belongs to from where it read it.
    /// </remarks>
    public DiscoveryRunId RunId { get; init; }

    /// <summary>Gets the place this holds in the run, counted from one.</summary>
    /// <remarks>Zero is what an event that has not been published yet carries, and no published event ever has it.</remarks>
    public long Sequence { get; init; }

    /// <summary>Gets the name this event is published under, which is the value the type discriminator carries.</summary>
    /// <remarks>
    /// <para>
    /// Declared here so a transport naming an event — a Server-Sent Events <c>event</c> field, say — uses the same word
    /// the JSON discriminator does rather than a second mapping that can disagree with it.
    /// </para>
    /// <para>
    /// <strong>Every override carries <see cref="JsonIgnoreAttribute" /> again.</strong> The serializer reads the
    /// attribute off the member it is serializing rather than off the one that member overrides, so an override without
    /// it writes the word a second time into the document beside the discriminator, and the same holds for
    /// <see cref="EndsTheRun" />. Both are how this run is read in process rather than anything a client is told.
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public abstract string EventName { get; }

    /// <summary>Gets whether this event ends the run, after which nothing further is published for it.</summary>
    /// <remarks>Overridden with <see cref="JsonIgnoreAttribute" /> repeated, for the reason <see cref="EventName" /> states.</remarks>
    [JsonIgnore]
    public virtual bool EndsTheRun => false;
}
