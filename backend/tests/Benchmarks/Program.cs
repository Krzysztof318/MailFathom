// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using BenchmarkDotNet.Running;

namespace MailFathom.Benchmarks;

/// <summary>Runs the hot-path benchmarks this project holds.</summary>
/// <remarks>
/// A switcher rather than a fixed list, so a nightly run asks for everything with <c>--filter *</c> while somebody
/// reading one path locally names it. The arguments are the library's own, which is what keeps this entrypoint from
/// growing a command line of its own.
/// </remarks>
internal static class Program
{
    private static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, new HotPathBenchmarkConfig());
}
