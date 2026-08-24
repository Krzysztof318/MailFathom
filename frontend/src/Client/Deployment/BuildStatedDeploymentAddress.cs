// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Deployment;

/// <summary>Takes the deployment address the build of this head stated, and asks the head itself when it stated none.</summary>
/// <remarks>
/// <para>
/// Not a head's own answer, which is why it wraps one rather than standing beside the two. Where a head looks for its
/// deployment is a property of the head — an installed one reads what somebody wrote, a served one already knows — and
/// this is the case where neither is true: a head started by an orchestration is served by a development server on a
/// socket of its own while the service listens on another, so the origin it was fetched from is a file server and
/// there is no installation to have written anything. The one thing that does know is whatever built it.
/// </para>
/// <para>
/// The channel is the runtime host configuration, because a browser reads no process environment and has no file
/// beside it. <c>Client.csproj</c> writes <see cref="ConfigurationKey" /> from an MSBuild property, the WebAssembly SDK
/// carries the runtime configuration into the boot document the page fetches, and the desktop head reads the same key
/// out of its own <c>runtimeconfig.json</c> — one mechanism, both heads, and no reflection for a trimmer to remove.
/// </para>
/// <para>
/// A build that states nothing writes no key, so this is absent from every artifact but one somebody deliberately
/// pointed: a bundle published into the container image resolves its deployment as the origin it was served from
/// exactly as it did before this existed.
/// </para>
/// </remarks>
internal sealed class BuildStatedDeploymentAddress : IDeploymentAddressSource
{
    /// <summary>The runtime host configuration key the address is written under.</summary>
    /// <remarks>
    /// The same name <c>frontend/src/Client/Client.csproj</c> emits, which is one decision recorded in two files
    /// because no build file is shared between a project and the app model that starts it. A rename reaching only one
    /// of them arrives as a head that quietly ignored the address it was given rather than as anything a build says.
    /// </remarks>
    public const string ConfigurationKey = "MailFathom.Client.DeploymentAddress";

    private readonly IDeploymentAddressSource head;
    private readonly string? stated;

    /// <summary>Initializes the source over the head that answers when the build stated no address.</summary>
    /// <param name="head">How this head answers for itself.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="head" /> is <see langword="null" />.</exception>
    public BuildStatedDeploymentAddress(IDeploymentAddressSource head)
        : this(head, AppContext.GetData(ConfigurationKey) as string)
    {
    }

    /// <summary>Initializes the source over a stated address rather than over the one this build carries.</summary>
    /// <param name="head">How this head answers for itself.</param>
    /// <param name="stated">The address the build stated, or <see langword="null" /> when it stated none.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="head" /> is <see langword="null" />.</exception>
    /// <remarks>What a test states, since the value the constructor above reads belongs to the whole process and a suite that wrote it would be deciding for every other test in the run.</remarks>
    internal BuildStatedDeploymentAddress(IDeploymentAddressSource head, string? stated)
    {
        ArgumentNullException.ThrowIfNull(head);

        this.head = head;
        this.stated = stated;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when the build stated something that is not an absolute address.</exception>
    /// <remarks>
    /// What is refused here is only the shape no route could be resolved against. Whether the address is one this
    /// client may carry a token to stays <c>Client.Backend</c>'s rule, which judges every head's answer alike: it
    /// permits clear text to loopback, which is what a local orchestration hands over, and refuses it to anything
    /// else.
    /// </remarks>
    public Uri Resolve(DeploymentSettings settings)
    {
        var built = this.stated?.Trim();

        if (string.IsNullOrEmpty(built))
        {
            return this.head.Resolve(settings);
        }

        return Uri.TryCreate(built, UriKind.Absolute, out var address)
            ? address
            : throw new InvalidOperationException(
                $"'{this.stated}' was built into this head as the deployment to reach, under the runtime host "
                + $"configuration key '{ConfigurationKey}', and is not an absolute address. Whatever built this head "
                + "has to state an origin — the scheme, host, and port and nothing else.");
    }
}
