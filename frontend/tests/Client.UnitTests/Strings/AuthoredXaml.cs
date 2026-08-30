// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using System.Xml.Linq;
using MailFathom.Client.Presentation;
using MailFathom.Client.Presentation.Settings;
using MailFathom.Client.Presentation.Spaces.Mail;
using MailFathom.Client.Presentation.Workspace;

namespace MailFathom.Client.UnitTests.Strings;

/// <summary>
/// Structural facts about the client's authored views, read from the views themselves.
/// </summary>
/// <remarks>
/// Bindings, templates, and visual states are names a running head resolves against a generated bindable, and a
/// misspelled one reaches somebody as an empty list or a control that never fires rather than as a failure. The
/// views are read here the way the tables are, from the files the project links beside the test assembly, because
/// this host has no visual tree to parse them into.
/// </remarks>
internal static class AuthoredXaml
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly Lazy<IReadOnlyList<AuthoredViewFile>> Loaded = new(Load);

    /// <summary>Every authored view the project copies beside the test assembly.</summary>
    public static IReadOnlyList<AuthoredViewFile> Files() => Loaded.Value;

    /// <summary>The view whose file name is <paramref name="fileName"/>.</summary>
    public static AuthoredViewFile File(string fileName) =>
        Files().Single(view => string.Equals(view.FileName, fileName, StringComparison.Ordinal));

    /// <summary>The model <see cref="App.RegisterRoutes"/> maps a view to, or the frame model a space binds from.</summary>
    /// <param name="fileName">The view's file name.</param>
    /// <returns>The model type, or <see langword="null"/> when the view is a control with its own properties.</returns>
    public static Type? MappedModel(string fileName) => fileName switch
    {
        "ConnectPage.xaml" => typeof(ConnectModel),
        "SignInPage.xaml" => typeof(SignInModel),
        "WorkspacePage.xaml" => typeof(WorkspaceModel),
        "SettingsPage.xaml" => typeof(SettingsModel),
        "MailPage.xaml" or "MailThreadPage.xaml" or "MailMessagePage.xaml"
            or "MailThreadView.xaml" or "MailSearchView.xaml" => typeof(MailModel),
        "DiscoverPage.xaml" or "CasesPage.xaml" => typeof(WorkspaceModel),
        _ => null,
    };

    /// <summary>Whether <paramref name="path"/> names a public member on <paramref name="model"/>, walking nested paths.</summary>
    public static bool NamesMember(Type model, string path)
    {
        var type = model;

        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (segment is "Parent" or "DataContext")
            {
                continue;
            }

            var member = FindMember(type, segment);
            if (member is null)
            {
                return false;
            }

            type = UnwrapFeed(MemberType(member));
        }

        return true;
    }

    private static AuthoredViewFile[] Load()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Xaml");
        Assert.True(Directory.Exists(root), root);

        var pages = Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories).ToArray();
        Assert.NotEmpty(pages);

        return
        [
            .. pages
                .Order(StringComparer.Ordinal)
                .Select(path => new AuthoredViewFile(Path.GetFileName(path), XDocument.Load(path))),
        ];
    }

    private static MemberInfo? FindMember(Type type, string name)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

        for (var current = type; current is not null; current = current.BaseType)
        {
            var property = current.GetProperty(name, flags | BindingFlags.DeclaredOnly);
            if (property is not null)
            {
                return property;
            }

            var method = current.GetMethod(name, flags | BindingFlags.DeclaredOnly);
            if (method is not null)
            {
                return method;
            }
        }

        return null;
    }

    private static Type MemberType(MemberInfo member) => member switch
    {
        PropertyInfo property => property.PropertyType,
        MethodInfo method => method.ReturnType,
        _ => typeof(object),
    };

    private static Type UnwrapFeed(Type type)
    {
        if (!type.IsGenericType)
        {
            return type;
        }

        var definition = type.GetGenericTypeDefinition();
        if (definition == typeof(IFeed<>)
            || definition == typeof(IState<>)
            || definition == typeof(IListFeed<>)
            || definition == typeof(IListState<>))
        {
            return type.GetGenericArguments()[0];
        }

        return type;
    }

    /// <summary>One authored XAML file and the structural facts a binding contract can ask of it.</summary>
    /// <param name="FileName">The file name, without the directory it was copied from.</param>
    /// <param name="Document">The document as authored.</param>
    internal sealed record AuthoredViewFile(string FileName, XDocument Document)
    {
        /// <summary>Every <c>FeedView</c> in the file.</summary>
        public IReadOnlyList<AuthoredFeedView> FeedViews() =>
        [
            .. this.Document.Descendants()
                .Where(element => element.Name.LocalName == "FeedView")
                .Select(element => new AuthoredFeedView(this.FileName, element)),
        ];

        /// <summary>Every <c>LoadingView</c> <c>Source</c> path that is a binding.</summary>
        public IReadOnlyList<string> LoadingViewSources() =>
        [
            .. this.Document.Descendants()
                .Where(element => element.Name.LocalName == "LoadingView")
                .Select(element => MarkupBinding.Parse(element.Attribute("Source")?.Value))
                .Where(binding => binding is { Path.Length: > 0 })
                .Select(binding => binding!.Path),
        ];

        /// <summary>Command bindings that name a model member rather than a control or a navigation request.</summary>
        public IReadOnlyList<MarkupBinding> ModelCommands() =>
        [
            .. this.Bindings()
                .Where(binding => binding.IsCommand && binding.TargetsMappedModel),
        ];

        /// <summary>Two-way bindings that write into the mapped model rather than into another element.</summary>
        public IReadOnlyList<MarkupBinding> ModelTwoWayBindings() =>
        [
            .. this.Bindings()
                .Where(binding => binding.IsTwoWay && binding.TargetsMappedModel),
        ];

        /// <summary>Every <c>VisualState</c> name authored in the file.</summary>
        public IReadOnlyList<string> VisualStateNames() =>
        [
            .. this.Document.Descendants()
                .Where(element => element.Name.LocalName == "VisualState")
                .Select(element => element.Attribute(Xaml + "Name")?.Value)
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => name!),
        ];

        /// <summary>Every <c>x:Name</c> in the file.</summary>
        public IReadOnlyList<string> NamedElements() =>
        [
            .. this.Document.Descendants()
                .Select(element => element.Attribute(Xaml + "Name")?.Value)
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => name!),
        ];

        /// <summary>Whether a descendant with this local name is authored.</summary>
        public bool HasElement(string localName) =>
            this.Document.Descendants().Any(element => element.Name.LocalName == localName);

        /// <summary>Whether any binding path in the file is <paramref name="path"/>, after stripping <c>Parent</c>/<c>DataContext</c>.</summary>
        public bool HasBindingPath(string path) =>
            this.Bindings().Any(binding =>
                string.Equals(binding.ModelPath, path, StringComparison.Ordinal));

        /// <summary>Whether any <c>x:Bind</c> path in the file is <paramref name="path"/>.</summary>
        public bool HasXBindPath(string path) =>
            this.Bindings().Any(binding =>
                binding.IsXBind && string.Equals(binding.Path, path, StringComparison.Ordinal));

        private IReadOnlyList<MarkupBinding> Bindings() =>
        [
            .. this.Document.Descendants()
                .SelectMany(element => element.Attributes().Select(attribute => (element, attribute)))
                .Select(pair => MarkupBinding.Parse(pair.attribute.Value, pair.attribute.Name.LocalName))
                .Where(binding => binding is not null)
                .Select(binding => binding!),
        ];
    }

    /// <summary>One <c>FeedView</c> as authored, with the templates and list bindings a contract can ask of it.</summary>
    /// <param name="ViewFile">The file the view was authored in.</param>
    /// <param name="Element">The <c>FeedView</c> element.</param>
    internal sealed record AuthoredFeedView(string ViewFile, XElement Element)
    {
        /// <summary>The <c>Source</c> binding path, or empty when none was authored.</summary>
        public string Source => MarkupBinding.Parse(this.Element.Attribute("Source")?.Value)?.Path ?? string.Empty;

        /// <summary>Whether a value template is authored, as a property element or as the content <c>DataTemplate</c>.</summary>
        public bool HasValueTemplate => this.ValueTemplate() is not null;

        /// <summary>Whether a progress template is authored.</summary>
        public bool HasProgressTemplate => this.HasPropertyTemplate("ProgressTemplate");

        /// <summary>Whether a none template is authored.</summary>
        public bool HasNoneTemplate => this.HasPropertyTemplate("NoneTemplate");

        /// <summary>Whether an error template is authored.</summary>
        public bool HasErrorTemplate => this.HasPropertyTemplate("ErrorTemplate");

        /// <summary>
        /// Whether the value template draws a list, which is the #1362 shape: a <c>ListView</c> or <c>ItemsControl</c>
        /// bound as an items source.
        /// </summary>
        public bool DrawsAList => this.ListItemsSources().Count > 0;

        /// <summary>Every list <c>ItemsSource</c> binding inside the value template.</summary>
        public IReadOnlyList<string> ListItemsSources()
        {
            var value = this.ValueTemplate();
            if (value is null)
            {
                return [];
            }

            return
            [
                .. value.Descendants()
                    .Where(element => element.Name.LocalName is "ListView" or "ItemsControl")
                    .Select(element => MarkupBinding.Parse(element.Attribute("ItemsSource")?.Value)?.Path)
                    .Where(path => !string.IsNullOrEmpty(path))
                    .Select(path => path!),
            ];
        }

        private bool HasPropertyTemplate(string name) =>
            this.Element.Elements().Any(element => element.Name.LocalName == $"FeedView.{name}");

        private XElement? ValueTemplate()
        {
            var property = this.Element.Elements()
                .FirstOrDefault(element => element.Name.LocalName == "FeedView.ValueTemplate");

            if (property is not null)
            {
                return property.Elements().FirstOrDefault();
            }

            return this.Element.Elements().FirstOrDefault(element => element.Name.LocalName == "DataTemplate");
        }
    }

    /// <summary>One markup extension that names a path.</summary>
    /// <param name="Path">The path as authored, including <c>Parent</c> or <c>DataContext</c> prefixes.</param>
    /// <param name="Attribute">The attribute local name the extension was written on.</param>
    /// <param name="IsTwoWay">Whether the binding writes back.</param>
    /// <param name="HasElementName">Whether the binding names another element rather than the mapped model.</param>
    /// <param name="IsXBind">Whether the extension is <c>x:Bind</c> rather than <c>Binding</c>.</param>
    internal sealed record MarkupBinding(
        string Path,
        string Attribute,
        bool IsTwoWay,
        bool HasElementName,
        bool IsXBind)
    {
        /// <summary>Whether this is a command binding rather than a navigation request or a parameter.</summary>
        public bool IsCommand =>
            this.Attribute is "Command" or "CommandExtensions.Command";

        /// <summary>The path a mapped model is asked for, with <c>Parent</c> and <c>DataContext</c> stripped.</summary>
        public string ModelPath
        {
            get
            {
                var path = this.Path;
                while (path.StartsWith("Parent.", StringComparison.Ordinal)
                    || path.StartsWith("DataContext.", StringComparison.Ordinal))
                {
                    path = path[(path.IndexOf('.', StringComparison.Ordinal) + 1)..];
                }

                return path;
            }
        }

        /// <summary>
        /// Whether the binding is against the mapped model rather than another element. An <c>ElementName</c> binding
        /// still is when the path walks <c>DataContext</c>, which is how a control reaches the page's model.
        /// </summary>
        public bool TargetsMappedModel =>
            this.Path.Length > 0
            && (!this.HasElementName || this.Path.StartsWith("DataContext.", StringComparison.Ordinal));

        /// <summary>Reads a markup extension, or <see langword="null"/> when the value is not a binding.</summary>
        public static MarkupBinding? Parse(string? value, string attribute = "")
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var markup = value.Trim();
            if (!markup.StartsWith('{') || !markup.EndsWith('}'))
            {
                return null;
            }

            var inner = markup[1..^1].Trim();
            var isXBind = inner.StartsWith("x:Bind", StringComparison.Ordinal);
            if (!isXBind && !inner.StartsWith("Binding", StringComparison.Ordinal))
            {
                return null;
            }

            var remainder = inner[(isXBind ? "x:Bind".Length : "Binding".Length)..].Trim();
            var parts = remainder.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            var path = string.Empty;
            var isTwoWay = false;
            var hasElementName = false;

            foreach (var part in parts)
            {
                if (part.StartsWith("Path=", StringComparison.Ordinal))
                {
                    path = part["Path=".Length..].Trim();
                }
                else if (part.StartsWith("Mode=", StringComparison.Ordinal))
                {
                    isTwoWay = string.Equals(part["Mode=".Length..].Trim(), "TwoWay", StringComparison.Ordinal);
                }
                else if (part.StartsWith("ElementName=", StringComparison.Ordinal))
                {
                    hasElementName = true;
                }
                else if (part.StartsWith("UpdateSourceTrigger=", StringComparison.Ordinal)
                    || part.StartsWith("Converter=", StringComparison.Ordinal)
                    || part.StartsWith("RelativeSource=", StringComparison.Ordinal)
                    || part.StartsWith("FallbackValue=", StringComparison.Ordinal)
                    || part.StartsWith("TargetNullValue=", StringComparison.Ordinal)
                    || part.StartsWith("StringFormat=", StringComparison.Ordinal)
                    || part.StartsWith("ConverterParameter=", StringComparison.Ordinal))
                {
                    continue;
                }
                else if (path.Length == 0 && !part.Contains('=', StringComparison.Ordinal))
                {
                    path = part;
                }
            }

            return new MarkupBinding(path, attribute, isTwoWay, hasElementName, isXBind);
        }
    }
}
