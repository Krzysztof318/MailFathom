// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Frozen;

namespace MailFathom.Mcp.Tools.Categories;

/// <summary>The categories one endpoint may publish, which is what a deployment states and what a client may narrow.</summary>
/// <remarks>
/// <para>
/// A deployment composes one of these from its own configuration and it is the authority: a tool outside it is absent
/// from every listing and its name answers nothing, whatever a caller presents and whatever a request asks for. What a
/// client may do is <see cref="NarrowedBy" />, which intersects and therefore only ever takes away.
/// </para>
/// <para>
/// It cannot turn anything on. A category naming a capability the deployment has not enabled publishes nothing, because
/// selecting a category removes descriptors and never adds one — the capability switches stay the authority over
/// whether a tool exists at all, and the grant stays the authority over whether this caller may reach it.
/// </para>
/// <para>
/// Composed once while the host is being composed and read from every request, so the instance is immutable and safe to
/// share across threads.
/// </para>
/// </remarks>
public sealed class PublishedToolCategorySelection
{
    private readonly FrozenSet<McpToolCategory> selected;

    private PublishedToolCategorySelection(IReadOnlySet<McpToolCategory> selected)
    {
        this.selected = selected.ToFrozenSet();
        this.Categories = [.. McpToolCategory.All.Where(selected.Contains)];
    }

    /// <summary>Gets the selection publishing every category, which is what a deployment that names none publishes.</summary>
    public static PublishedToolCategorySelection Everything { get; } = new(McpToolCategory.All.ToHashSet());

    /// <summary>Gets the selected categories, in the order <see cref="McpToolCategory.All" /> declares them.</summary>
    /// <remarks>Empty only for a selection <see cref="NarrowedBy" /> produced, which is a client having asked for a category this deployment does not publish.</remarks>
    public IReadOnlyList<McpToolCategory> Categories { get; }

    /// <summary>Composes the selection a deployment named.</summary>
    /// <param name="categories">The categories the deployment wrote, in any order and with repetition permitted.</param>
    /// <returns>The selection, which is <see cref="Everything" /> when the deployment named none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="categories" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when a category is the unspecified struct default, which names nothing to publish.</exception>
    /// <remarks>
    /// Naming none publishes everything, which is the behaviour a deployment has without the setting, so its absence
    /// changes nothing about an endpoint that already exists. An endpoint that should publish no tool at all is an
    /// endpoint that is not served, which is what <c>McpEndpoint:Enabled</c> decides.
    /// </remarks>
    public static PublishedToolCategorySelection Of(IEnumerable<McpToolCategory> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);

        var named = categories.ToHashSet();

        if (named.Contains(default))
        {
            throw new ArgumentException(
                "A published category must be one this surface declares rather than the unspecified default.",
                nameof(categories));
        }

        return named.Count is 0 ? Everything : new PublishedToolCategorySelection(named);
    }

    /// <summary>Reports whether this endpoint publishes a category at all.</summary>
    /// <param name="category">The category a tool declared.</param>
    /// <returns><see langword="true" /> when the selection carries it.</returns>
    /// <remarks>The unspecified default is carried by no selection, so a tool that declared no category is published by none.</remarks>
    public bool Publishes(McpToolCategory category) => this.selected.Contains(category);

    /// <summary>Narrows the selection to what a client asked its own session for.</summary>
    /// <param name="requested">The categories the client named, empty where it named none this surface publishes.</param>
    /// <returns>The selection in force for that request.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="requested" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// The result is the intersection, so a client can only ever be served fewer tools than the deployment publishes. A
    /// category this deployment excluded stays excluded however the request is written, and a request naming <b>only</b>
    /// excluded categories is therefore served nothing at all rather than being widened back to the configured set —
    /// which is the honest answer to a client that asked for a surface this endpoint does not offer.
    /// </para>
    /// <para>
    /// An empty request is a request that named nothing this surface publishes, including one that named nothing at all,
    /// and leaves the deployment's own selection in force. That keeps a malformed or unknown value from narrowing an
    /// endpoint to silence, which is the failure a caller could not tell from a broken deployment.
    /// </para>
    /// </remarks>
    public PublishedToolCategorySelection NarrowedBy(IReadOnlySet<McpToolCategory> requested)
    {
        ArgumentNullException.ThrowIfNull(requested);

        return requested.Count is 0
            ? this
            : new PublishedToolCategorySelection(this.selected.Where(requested.Contains).ToHashSet());
    }
}
