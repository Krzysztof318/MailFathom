// Copyright © 2026 Krzysztof Kasprowicz

using System.Collections;
using System.Reflection;

namespace MailMcp.Infrastructure.Secrets;

/// <summary>Finds every secret-bearing setting in a bound options graph.</summary>
/// <remarks>
/// <para>
/// This is what makes <see cref="ConfiguredSecret" /> useful as a marker: the walk discovers settings by type, so a
/// secret-bearing setting added long after this code was written is validated, resolved, and erased without anyone
/// registering it. An explicit call list cannot make that guarantee, which is the measured need that justifies
/// reflection here. The walk runs once, at startup, over a bound options object of a few dozen properties.
/// </para>
/// <para>
/// It descends only into types this repository owns, so it cannot wander into a framework object graph, and it guards
/// against a cycle because options types are ordinary classes and nothing forbids a back-reference.
/// </para>
/// </remarks>
public static class ConfiguredSecretDiscovery
{
    private const string OwnedAssemblyPrefix = "MailMcp.";

    /// <summary>Walks a bound options object and reports its secret-bearing settings.</summary>
    /// <param name="boundOptions">The bound options root, for example the object behind the <c>MailSynchronization</c> section.</param>
    /// <param name="rootConfigurationPath">The configuration path of that root, which prefixes every reported path.</param>
    /// <returns>The discovered blocks and the paths of raw string properties that name a secret.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="boundOptions" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rootConfigurationPath" /> is <see langword="null" />, empty, or whitespace.</exception>
    public static DiscoveredSecretSettings FindSecretBearingSettings(object boundOptions, string rootConfigurationPath)
    {
        ArgumentNullException.ThrowIfNull(boundOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootConfigurationPath);

        var walk = new OptionsGraphWalk();
        walk.Descend(boundOptions, rootConfigurationPath);

        return new DiscoveredSecretSettings(walk.Blocks, walk.RawSecretPropertyPaths);
    }

    private sealed class OptionsGraphWalk
    {
        private readonly HashSet<object> visited = new(ReferenceEqualityComparer.Instance);

        internal List<DiscoveredSecret> Blocks { get; } = [];

        internal List<string> RawSecretPropertyPaths { get; } = [];

        internal void Descend(object instance, string path)
        {
            if (!this.visited.Add(instance))
            {
                return;
            }

            foreach (var property in ReadableProperties(instance.GetType()))
            {
                var value = property.GetValue(instance);
                if (value is null)
                {
                    continue;
                }

                this.Visit(value, $"{path}:{property.Name}", property.Name);
            }
        }

        private void Visit(object value, string path, string propertyName)
        {
            switch (value)
            {
                case ConfiguredSecret block:
                    this.CollectBlock(block, path);

                    break;

                case string:
                    if (SecretPropertyNaming.NamesASecret(propertyName))
                    {
                        this.RawSecretPropertyPaths.Add(path);
                    }

                    break;

                case IEnumerable elements:
                    this.DescendIntoElements(elements, path);

                    break;

                default:
                    if (IsOwnedOptionsType(value.GetType()))
                    {
                        this.Descend(value, path);
                    }

                    break;
            }
        }

        private void CollectBlock(ConfiguredSecret block, string path)
        {
            if (!this.visited.Add(block))
            {
                return;
            }

            this.Blocks.Add(new DiscoveredSecret(path, block));

            if (block.Password is { } nestedPassword)
            {
                this.CollectBlock(nestedPassword, $"{path}:{nameof(ConfiguredSecret.Password)}");
            }
        }

        private void DescendIntoElements(IEnumerable elements, string path)
        {
            var index = 0;
            foreach (var element in elements)
            {
                if (element is not null)
                {
                    this.Visit(element, $"{path}:{index}", propertyName: string.Empty);
                }

                index++;
            }
        }

        private static IEnumerable<PropertyInfo> ReadableProperties(Type type) => type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0);

        private static bool IsOwnedOptionsType(Type type) => !type.IsValueType
            && type.Assembly.GetName().Name?.StartsWith(OwnedAssemblyPrefix, StringComparison.Ordinal) == true;
    }
}
