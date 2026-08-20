// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Mcp.Tools.Categories;

/// <summary>One kind of thing this surface offers, which a deployment may name to publish that kind and no other.</summary>
/// <remarks>
/// <para>
/// The type is a closed enumeration of values rather than a C# <see langword="enum" />, because the name is the identity
/// and it travels outside this process: an operator writes it in configuration and a client writes it in the header
/// <see cref="McpToolCategoryHeader" /> defines. A member's ordinal would mean nothing to either, and its C# name has to
/// be free to change without moving what an operator has already written.
/// </para>
/// <para>
/// A category names what a tool is <b>for</b> rather than which project implements it or which permission reaches it.
/// The two questions are separate on purpose: a grant decides what a caller may do, and a category decides what this
/// endpoint offers at all. Neither is derivable from the other — the contact book is read under one permission and
/// written under a second while both belong to one category, and a deployment publishing the mailbox alone withholds
/// both from a caller granted everything.
/// </para>
/// <para>
/// The set is closed so that a category naming nothing is unknown rather than new: startup refuses a misspelling in
/// configuration instead of narrowing an endpoint to a name no tool carries, and a client asking for one is answered
/// with the selection it would have had. Adding a member is a deliberate change here, made when the tools it groups
/// exist, and it is a change to the configuration schema as much as to this file.
/// </para>
/// <para>
/// Nothing serializes a category, which is why the type carries no JSON converter: it is parsed from configuration and
/// from a request header through <see cref="TryParse" />, and no tool descriptor, result, or record publishes one. Being
/// a struct, <see langword="default" /> is reachable and is not a category; it reports itself through
/// <see cref="IsSpecified" /> and is refused by <see cref="PublishedToolCategorySelection.Of" />, which is the one
/// place a set of them is composed.
/// </para>
/// </remarks>
public readonly record struct McpToolCategory
{
    private readonly string? name;

    private McpToolCategory(string name) => this.name = name;

    /// <summary>Gets the category of the tools that read the local mailbox copy: the accounts, the timeline, one email's content, and search.</summary>
    /// <remarks>It is the retrieval surface and the one a deployment standing an instance up for reading keeps on its own. Nothing in it reaches a mail server or changes anything.</remarks>
    public static McpToolCategory Mailbox { get; } = new("mailbox");

    /// <summary>Gets the category of the tool that changes the flags and keywords on mail this deployment holds.</summary>
    /// <remarks>
    /// Apart from <see cref="Mailbox" /> because marking mail is not reading it: the effect reaches the owner's own mail
    /// server and is visible in every client they open, which is exactly what a deployment publishing the reading
    /// surface alone means to withhold.
    /// </remarks>
    public static McpToolCategory Flags { get; } = new("flags");

    /// <summary>Gets the category of the tools that send mail from an account this deployment holds, of the two over a send that was queued, and of dispatching a draft.</summary>
    /// <remarks>
    /// Reading back a send and withdrawing one belong here rather than with the mailbox, because both are about a
    /// message this deployment was asked to send: a deployment that sends nothing has no outbox for either to answer
    /// about. Dispatching a draft belongs here for the same reason it requires the sending grant rather than the
    /// drafting one — what it does is put mail on the wire — which is what lets a deployment publish
    /// <see cref="Drafts" /> so an agent composes for somebody to review and withhold this one so nothing it wrote
    /// leaves on its own.
    /// </remarks>
    public static McpToolCategory Sending { get; } = new("sending");

    /// <summary>Gets the category of the tools that compose, amend, and discard a message that is never sent.</summary>
    /// <remarks>Apart from <see cref="Sending" /> because a draft leaves nothing: it is the surface an operator publishes when an agent should prepare mail a person then reads before anything goes out.</remarks>
    public static McpToolCategory Drafts { get; } = new("drafts");

    /// <summary>Gets the category of the tool that answers a question from mail content by sending it to a model provider.</summary>
    /// <remarks>Its own category because it is the one kind of tool here whose work leaves the deployment for a provider, which a deployment may decline to offer while publishing everything that reads the same mail locally.</remarks>
    public static McpToolCategory Answering { get; } = new("answering");

    /// <summary>Gets the category of the tools that read and write the contact book.</summary>
    /// <remarks>The book is an assembled record about identified third parties rather than mail that arrived, so a deployment decides separately whether this endpoint offers it at all.</remarks>
    public static McpToolCategory Contacts { get; } = new("contacts");

    /// <summary>Gets every published category.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<McpToolCategory> All { get; } =
    [
        Mailbox,
        Flags,
        Sending,
        Drafts,
        Answering,
        Contacts,
    ];

    /// <summary>Gets whether this value names a published category rather than the unusable struct default.</summary>
    public bool IsSpecified => this.name is not null;

    /// <summary>Gets the published name, which is what an operator writes and what a client names in a header.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a category.</exception>
    public string Name => this.name
        ?? throw new InvalidOperationException("The value is the default of the struct and does not name a tool category.");

    /// <summary>Parses an operator-supplied or client-supplied category name.</summary>
    /// <param name="name">The written name, which may be absent or anything at all.</param>
    /// <param name="category">The parsed category when the name is published; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the name is one this surface publishes; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// The comparison ignores case and surrounding whitespace, unlike a permission name, which is compared byte for byte
    /// because it also has to travel as an OAuth scope. A category travels as a configuration value and as a header
    /// token and as nothing else, so a spelling this accepts is a spelling nothing downstream can read differently —
    /// which makes case-folding here a kindness rather than a second meaning.
    /// </remarks>
    public static bool TryParse(string? name, out McpToolCategory category)
    {
        // No published category is the struct default, so an unmatched name yields the unspecified value the caller
        // already receives when parsing fails.
        category = name is null
            ? default
            : All.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

        return category.IsSpecified;
    }

    /// <summary>Reports every published name, in declaration order, for a failure that has to say what is accepted.</summary>
    /// <returns>The names, separated by commas.</returns>
    public static string PublishedNames() => string.Join(", ", All.Select(category => category.Name));

    /// <inheritdoc />
    public override string ToString() => this.name ?? "(unspecified)";
}
