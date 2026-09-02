// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using MailFathom.Application.Resilience;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Configuration.OwnerSettings;
using MailFathom.Host.Configuration.Provisioning;
using MailFathom.Infrastructure;
using MailFathom.Infrastructure.Secrets.Resolution;

namespace MailFathom.PublicSurfaces.UnitTests;

/// <summary>Renders every configuration key an operator may write, in a form two builds compare byte for byte.</summary>
/// <remarks>
/// <para>
/// The sections are discovered rather than listed, from the <c>SectionName</c> constant every bound options class
/// declares. A list would be the same hand-written record the golden file exists to replace: a section added without a
/// line in it would leave its keys outside the record, which is the failure mode this suite is for.
/// </para>
/// <para>
/// What a walk produces is the keys the configuration binder can write, which is why a property is followed only where
/// the binder could set it — a public setter, or a collection it populates in place. A computed property has no key
/// behind it, and emitting one would put a setting into the record that an operator writing it would find ignored.
/// </para>
/// </remarks>
internal static class ConfigurationKeySurface
{
    /// <summary>The single key that selects how a secret reference is read, which the host reads without binding a section.</summary>
    private const string SecretInterpretationKey = "Secrets:Interpretation";

    /// <summary>How deep a walk follows nested settings before it reports the path instead of continuing.</summary>
    /// <remarks>A backstop rather than a bound anything reaches: the cycle guard already stops a graph that refers to itself, and no configured section nests this far.</remarks>
    private const int MaximumDepth = 12;

    /// <summary>The discovered sections the host binds a list of rather than one of.</summary>
    /// <remarks>
    /// A section's own constant names where it is read from and says nothing about the shape beneath it, so nothing a
    /// walk can see distinguishes a section bound once from one bound per element. Naming the collections here is what
    /// records their keys as an operator writes them — <c>Accounts:0:Id</c> rather than <c>Accounts:Id</c> — and a
    /// second one added without a line here would be recorded under a path nobody can write.
    /// </remarks>
    private static readonly string[] SectionsBoundAsCollections = [DeclaredOwnerOptions.SectionName];

    /// <summary>Renders the published configuration key set.</summary>
    /// <returns>The header and one line per key, ordered so the rendering depends on nothing but the keys themselves.</returns>
    public static string Render()
    {
        var nullability = new NullabilityInfoContext();

        var keys = BoundSections()
            .SelectMany(section => Walk(section.Path, section.Settings, [section.Settings], nullability))
            .Concat(KeysReadWithoutTheBinder())
            .Select(key => key.ToString())
            .Order(StringComparer.Ordinal);

        return string.Join('\n', [.. Header, .. keys]);
    }

    /// <summary>What the file says about itself, so a reader who opened it first knows what it records.</summary>
    private static IEnumerable<string> Header =>
    [
        "# The MailFathom configuration key set, as the host binds it.",
        "#",
        "# One line per key an operator may write: the key, the type it binds as, and whether the section refuses to",
        "# start without it. A ':<index>' segment is a position in a list and a ':<key>' segment is a name the operator",
        "# chooses. Regenerate this file with:",
        "#",
        $"#   {PublicSurfaceGolden.RegenerationVariable}=1 dotnet test --project backend/tests/PublicSurfaces.UnitTests",
        "#",
        "# A change here is a change to a public surface, so it belongs in the pull request's own account of the break.",
        "# The framework-shaped sections are absent because they are not MailFathom's: Logging, ConnectionStrings, and",
        "# the Kestrel endpoints, which this host refuses rather than binds.",
        "#",
    ];

    /// <summary>Names every section bound into an options class, with the class the keys beneath it are read from.</summary>
    /// <remarks>
    /// The resilience settings are reached by name rather than by type, because they are bound in the infrastructure
    /// boundary from a class that stays internal to it. Resolving the type here fails loudly if it is renamed, which is
    /// what keeps the section in the record instead of quietly dropping out of it.
    /// </remarks>
    private static IEnumerable<(string Path, Type Settings)> BoundSections()
    {
        var resilienceSettings = typeof(ServiceCollectionExtensions).Assembly
            .GetType("MailFathom.Infrastructure.Resilience.OutboundDependencyResilienceOptions", throwOnError: true)!;

        return
        [
            .. typeof(McpEndpointOptions).Assembly
                .GetTypes()
                // Read key by key rather than bound, and deliberately under names its properties do not carry, so its
                // two keys are stated below instead of walked from a shape that would name them wrongly.
                .Where(candidate => candidate != typeof(ProvisionedConfigurationPaths))
                .Select(candidate => (Settings: candidate, Declaration: SectionNameDeclaration(candidate)))
                .Where(candidate => candidate.Declaration is not null)
                .Select(candidate => (PathOf((string)candidate.Declaration!.GetRawConstantValue()!), candidate.Settings)),

            // One bound instance per dependency class, named after the enumeration member, which is what makes the
            // member names part of the configuration surface rather than an implementation detail behind it.
            .. Enum.GetNames<OutboundDependency>()
                .Select(dependency => ($"Resilience:{dependency}", resilienceSettings)),
        ];
    }

    /// <summary>Reads the path a section's keys hang under, which carries a position where the host binds a list of it.</summary>
    private static string PathOf(string sectionName) =>
        SectionsBoundAsCollections.Contains(sectionName, StringComparer.Ordinal)
            ? $"{sectionName}:<index>"
            : sectionName;

    /// <summary>The keys the host reads straight from configuration, which no options class carries.</summary>
    private static IEnumerable<ConfigurationKey> KeysReadWithoutTheBinder() =>
    [
        new(ProvisionedConfigurationPaths.DirectoryKey, "string?", Required: false),
        new(ProvisionedConfigurationPaths.FileKey, "string?", Required: false),
        new(SecretInterpretationKey, TypeName(typeof(SecretValueInterpretation), nullable: false), Required: false),
    ];

    /// <summary>Reads the constant a bound options class names its section with, or nothing when the class declares none.</summary>
    private static FieldInfo? SectionNameDeclaration(Type candidate)
    {
        var declaration = candidate.GetField(
            nameof(McpEndpointOptions.SectionName),
            BindingFlags.Public | BindingFlags.Static);

        return declaration is { IsLiteral: true } && declaration.FieldType == typeof(string) ? declaration : null;
    }

    /// <summary>Walks one settings class, emitting a key for every leaf the binder can write beneath the path.</summary>
    private static IEnumerable<ConfigurationKey> Walk(
        string path,
        Type settings,
        IReadOnlyCollection<Type> ancestors,
        NullabilityInfoContext nullability)
    {
        foreach (var property in BindableProperties(settings))
        {
            var required = property.GetCustomAttribute<RequiredAttribute>() is not null;
            var nullable = nullability.Create(property).ReadState is NullabilityState.Nullable;

            foreach (var key in WalkValue(
                $"{path}:{property.Name}",
                property.PropertyType,
                nullable,
                required,
                ancestors,
                nullability))
            {
                yield return key;
            }
        }
    }

    /// <summary>Walks one value: a leaf as itself, a collection through its element, and a settings class through its own properties.</summary>
    private static IEnumerable<ConfigurationKey> WalkValue(
        string path,
        Type valueType,
        bool nullable,
        bool required,
        IReadOnlyCollection<Type> ancestors,
        NullabilityInfoContext nullability)
    {
        var underlying = Nullable.GetUnderlyingType(valueType) ?? valueType;

        if (IsLeaf(underlying))
        {
            return [new(path, TypeName(underlying, nullable || underlying != valueType), required)];
        }

        // Below this point the flag stops travelling. A requirement belongs to the property that carries the attribute,
        // and no key beneath a list or a nested class is the one an operator satisfied by writing it.
        if (DictionaryValueType(underlying) is { } dictionaryValue)
        {
            return WalkValue($"{path}:<key>", dictionaryValue, nullable: false, required: false, ancestors, nullability);
        }

        if (ElementType(underlying) is { } element)
        {
            return WalkValue($"{path}:<index>", element, nullable: false, required: false, ancestors, nullability);
        }

        // A settings class that reaches itself, directly or through another, has no finite key set; the path is reported
        // as the record of where the graph closes rather than walked until the depth backstop stops it.
        if (ancestors.Contains(underlying) || ancestors.Count >= MaximumDepth)
        {
            return [new(path, $"<recursive {underlying.Name}>", Required: false)];
        }

        return Walk(path, underlying, [.. ancestors, underlying], nullability);
    }

    /// <summary>Names the properties the configuration binder can write beneath a settings class.</summary>
    /// <remarks>
    /// A public setter is what the binder assigns through. A getter-only property is followed only where it hands back a
    /// mutable collection, which the binder adds to in place; a getter-only value or a read-only projection is computed
    /// from other keys and is no key of its own.
    /// </remarks>
    private static IEnumerable<PropertyInfo> BindableProperties(Type settings) => settings
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => property.GetIndexParameters().Length == 0)
        .Where(property => property.GetMethod?.IsPublic is true)
        .Where(property => property.SetMethod?.IsPublic is true || IsPopulatedInPlace(property.PropertyType))
        .OrderBy(property => property.Name, StringComparer.Ordinal);

    /// <summary>Whether the binder writes through a getter-only property by filling the instance already behind it.</summary>
    /// <remarks>
    /// The binder assigns through a setter and, where there is none, binds into the value the getter hands back. That
    /// reaches a nested settings class held as <c>{ get; } = new()</c> and a mutable collection it adds entries to, and
    /// stops at a read-only sequence, whose bound replacement it would have nowhere to put — which is what separates a
    /// configured list from a projection computed over other keys.
    /// </remarks>
    private static bool IsPopulatedInPlace(Type propertyType)
    {
        if (IsLeaf(Nullable.GetUnderlyingType(propertyType) ?? propertyType))
        {
            return false;
        }

        var mutableCollection = Interfaces(propertyType).Any(
            contract => contract.GetGenericTypeDefinition() == typeof(ICollection<>)
                || contract.GetGenericTypeDefinition() == typeof(IDictionary<,>));

        return mutableCollection || (!propertyType.IsArray && ElementType(propertyType) is null);
    }

    /// <summary>Reads the value type of a dictionary-shaped setting, whose keys the operator names.</summary>
    private static Type? DictionaryValueType(Type valueType) => Interfaces(valueType)
        .Where(contract => contract.GetGenericTypeDefinition() == typeof(IDictionary<,>)
            || contract.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
        .Select(contract => contract.GetGenericArguments()[1])
        .FirstOrDefault();

    /// <summary>Reads the element type of a list-shaped setting, whose entries the operator writes by position.</summary>
    private static Type? ElementType(Type valueType) => valueType.IsArray
        ? valueType.GetElementType()
        : Interfaces(valueType)
            .Where(contract => contract.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            .Select(contract => contract.GetGenericArguments()[0])
            .FirstOrDefault();

    /// <summary>The constructed generic interfaces a type carries, including itself when it is one.</summary>
    private static IEnumerable<Type> Interfaces(Type valueType) => valueType
        .GetInterfaces()
        .Append(valueType)
        .Where(contract => contract.IsInterface && contract.IsGenericType);

    /// <summary>Whether a value binds from one configuration string rather than from keys beneath it.</summary>
    private static bool IsLeaf(Type valueType) =>
        valueType.IsPrimitive
        || valueType.IsEnum
        || valueType == typeof(string)
        || valueType == typeof(decimal)
        || valueType == typeof(Guid)
        || valueType == typeof(TimeSpan)
        || valueType == typeof(DateOnly)
        || valueType == typeof(TimeOnly)
        || valueType == typeof(DateTime)
        || valueType == typeof(DateTimeOffset)
        || valueType == typeof(Uri)
        || valueType == typeof(Version);

    /// <summary>Names a bound type the way an operator has to write a value for it.</summary>
    /// <remarks>
    /// An enumeration is named with its members, because the spellings a value binds from are as much part of the
    /// surface as the key is: a renamed member fails a deployment that wrote the old name, and a diff here is what says
    /// so before a release does.
    /// </remarks>
    private static string TypeName(Type valueType, bool nullable)
    {
        var name = valueType switch
        {
            _ when valueType == typeof(string) => "string",
            _ when valueType == typeof(bool) => "bool",
            _ when valueType == typeof(int) => "int",
            _ when valueType == typeof(long) => "long",
            _ when valueType == typeof(double) => "double",
            _ when valueType == typeof(float) => "float",
            _ when valueType == typeof(decimal) => "decimal",
            _ when valueType == typeof(short) => "short",
            _ when valueType == typeof(byte) => "byte",
            _ when valueType == typeof(char) => "char",
            _ when valueType.IsEnum => $"{valueType.Name}({string.Join('|', Enum.GetNames(valueType))})",
            _ => valueType.Name,
        };

        return nullable ? $"{name}?" : name;
    }

    /// <summary>One key of the published configuration surface.</summary>
    /// <param name="Key">The key in configuration-section form.</param>
    /// <param name="Type">What an operator writes for it.</param>
    /// <param name="Required">Whether the section is refused without it.</param>
    private sealed record ConfigurationKey(string Key, string Type, bool Required)
    {
        /// <inheritdoc />
        public override string ToString() => string.Create(
            CultureInfo.InvariantCulture,
            $"{this.Key} = {this.Type} ({(this.Required ? "required" : "optional")})");
    }
}
