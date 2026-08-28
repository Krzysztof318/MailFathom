// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.SensitiveContent.Detection;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Configuration.RootSettings;

/// <summary>Binds and validates a candidate configuration by the rules a start applies to the real one.</summary>
/// <remarks>
/// <para>
/// The rules are not restated here. <see cref="BoundSettings" /> is the one place the sections, the strict binding,
/// the data annotations, and the custom validators are declared, and <see cref="ComposedSettings" /> is the one place
/// the rules a start takes before a container exists are; this registers the first into a container of its own over
/// the candidate configuration and asks the second about the candidate directly. A second list would be how a setting
/// comes to bind at startup and not at a write, or the reverse — and either way an operator would learn about it from
/// a deployment that stopped.
/// </para>
/// <para>
/// The container is thrown away with the answer. Nothing resolved from it ever runs: the options are materialized to
/// be judged, the failures are collected, and the provider is disposed, so a candidate that would have configured a
/// scanner, a client, or a worker configures none of them. The two dependencies the custom validators need are handed
/// in from the running process rather than rebuilt, because what a deployment registered is a property of the
/// deployment rather than of the candidate: the clock and the scanners a write is judged against are the ones the
/// process actually has.
/// </para>
/// <para>
/// Four shapes of failure arrive and all four are an operator's to fix. A data annotation or a custom validator
/// refusing arrives as <see cref="OptionsValidationException" />, several of them as an
/// <see cref="AggregateException" /> over those, and the binder refusing — an unknown property, a segment that is not
/// the array position it was written as, a value that will not convert — as an
/// <see cref="InvalidOperationException" /> that stops the pass where it stood. The fourth is that same type raised
/// while the sections are being *registered*, by a registration that reads a value or refuses a section naming
/// nothing, which is why the registration is guarded as well as the validation. Only the framework's own sentence is
/// carried from either of the last two, never the inner failure, which is where a value would be.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this validator.")]
internal sealed class CandidateSettingsValidator(
    TimeProvider timeProvider,
    IEnumerable<ISensitiveContentCatalog> sensitiveContentCatalogs)
{
    /// <summary>Finds what an operator must change before a candidate configuration could be the deployment's.</summary>
    /// <param name="candidate">The composed configuration the candidate document would produce.</param>
    /// <returns>One sentence per refused setting, empty when the candidate binds and validates.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="candidate" /> is <see langword="null" />.</exception>
    public IReadOnlyList<string> FindErrors(IConfiguration candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        // Both halves of what a start judges, and the composed half first because it is the half a start takes before a
        // container exists at all: a candidate turning every surface off, or naming a rule condition the compiler
        // refuses, would otherwise commit and stop the next start.
        return
        [
            .. this.FindComposedErrorsIn(candidate),
            .. this.FindBoundErrorsIn(candidate),
        ];
    }

    private IReadOnlyList<string> FindComposedErrorsIn(IConfiguration candidate)
    {
        try
        {
            return [.. ComposedSettings.FindRefusals(candidate, timeProvider).SelectMany(refusal => refusal.Errors)];
        }
        catch (InvalidOperationException refusal)
        {
            return [refusal.Message];
        }
    }

    /// <summary>Registers the bound sections over the candidate and runs the validators a start runs.</summary>
    /// <remarks>
    /// The registration is guarded as well as the validation, because part of what a start refuses over is decided
    /// while the sections are being registered rather than when they are validated: a section naming no dependency
    /// class, and a value a registration converts as it reads it. Left to escape, that would leave the write raising
    /// the refusal instead of returning it, which is the one outcome this port exists to replace.
    /// </remarks>
    private IReadOnlyList<string> FindBoundErrorsIn(IConfiguration candidate)
    {
        var services = new ServiceCollection();

        services.AddSingleton(timeProvider);

        foreach (var catalog in sensitiveContentCatalogs)
        {
            services.AddSingleton(catalog);
        }

        try
        {
            BoundSettings.AddTo(services, candidate);
        }
        catch (InvalidOperationException refusal)
        {
            return [refusal.Message];
        }

        using var provider = services.BuildServiceProvider();

        return FindValidatorErrorsIn(provider);
    }

    private static IReadOnlyList<string> FindValidatorErrorsIn(IServiceProvider candidateServices)
    {
        try
        {
            candidateServices.GetRequiredService<IStartupValidator>().Validate();

            return [];
        }
        catch (OptionsValidationException refusal)
        {
            return [.. refusal.Failures];
        }
        catch (AggregateException refusals)
        {
            return [.. refusals.InnerExceptions.OfType<OptionsValidationException>().SelectMany(refusal => refusal.Failures)];
        }
        catch (InvalidOperationException refusal)
        {
            return [refusal.Message];
        }
    }
}
