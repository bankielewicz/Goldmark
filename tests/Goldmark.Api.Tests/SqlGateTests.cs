using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.RegularExpressions;
using Goldmark.Api.Data;

namespace Goldmark.Api.Tests;

/// <summary>
/// NFR-SEC-3 build gate (DEC-040): every Dapper call goes through <see cref="DbSession"/>, whose SQL
/// arguments must be compile-time constants (CA1857). These checks keep the gate from being bypassed
/// or switched off; <c>tests/sql-gate/check-sql-gate.sh</c> proves a violating call fails the build.
/// </summary>
public sealed partial class SqlGateTests
{
    private static readonly string Root = SchemaMigrationsTests.RepositoryRoot();
    private const string Wrapper = "src/Goldmark.Api/Data/DbSession.cs";

    [GeneratedRegex(@"\bDapper\b", RegexOptions.CultureInvariant)]
    private static partial Regex DapperName();

    [GeneratedRegex(@"CA1857", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Ca1857();

    [Fact]
    public void Only_DbSession_uses_Dapper()
    {
        var offenders = SourceFiles("src", "*.cs")
            .Where(f => f != Wrapper)
            .Where(f => CodeLines(f).Any(line => DapperName().IsMatch(line)))
            .ToList();

        Assert.Empty(offenders);
        Assert.Contains(CodeLines(Wrapper), line => line.Trim() == "using Dapper;");
    }

    [Fact]
    public void Every_SQL_argument_of_DbSession_must_be_a_constant()
    {
        var sqlParameters = typeof(DbSession).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetParameters().Where(p => p.ParameterType == typeof(string)).Select(p => (Method: m.Name, Parameter: p)))
            .ToList();

        Assert.NotEmpty(sqlParameters);
        Assert.All(sqlParameters, p =>
        {
            Assert.Equal("sql", p.Parameter.Name);
            Assert.True(p.Parameter.GetCustomAttribute<ConstantExpectedAttribute>() is not null,
                $"{p.Method}({p.Parameter.Name}) lacks [ConstantExpected]");
        });
    }

    [Fact]
    public void CA1857_is_never_suppressed_or_downgraded()
    {
        // Source suppressions (#pragma, SuppressMessage), .editorconfig severities and MSBuild
        // properties (NoWarn, WarningsNotAsErrors) anywhere in the repository's own files.
        string[] patterns = ["*.cs", "*.props", "*.targets", "*.csproj", ".editorconfig", "*.globalconfig"];
        var offenders = patterns
            .SelectMany(p => SourceFiles(".", p))
            .Where(f => f != "tests/Goldmark.Api.Tests/SqlGateTests.cs" && !f.StartsWith("tests/sql-gate/", StringComparison.Ordinal))
            .Where(f => SettingLines(f).Any(line => Ca1857().IsMatch(line)))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Analyzers_run_with_warnings_as_errors()
    {
        var props = File.ReadAllText(Path.Combine(Root, "Directory.Build.props"));

        Assert.Contains("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", props, StringComparison.Ordinal);
        Assert.Contains("<AnalysisLevel>latest-recommended</AnalysisLevel>", props, StringComparison.Ordinal);
    }

    /// <summary>
    /// Repository-relative paths with forward slashes. Build output, dependencies and the web app
    /// (no .NET files) are not entered at all, which keeps the walk fast on slow file systems.
    /// </summary>
    private static IEnumerable<string> SourceFiles(string directory, string pattern)
    {
        var pending = new Stack<string>([Path.GetFullPath(Path.Combine(Root, directory))]);
        while (pending.TryPop(out var current))
        {
            foreach (var file in Directory.EnumerateFiles(current, pattern))
            {
                yield return Path.GetRelativePath(Root, file).Replace('\\', '/');
            }

            foreach (var child in Directory.EnumerateDirectories(current))
            {
                if (Path.GetFileName(child) is not ("bin" or "obj" or "node_modules" or ".git" or "tmp" or "web" or "devforgeai"))
                {
                    pending.Push(child);
                }
            }
        }
    }

    /// <summary>The file's lines without whole-line comments (a comment may name Dapper).</summary>
    private static IEnumerable<string> CodeLines(string file) =>
        File.ReadLines(Path.Combine(Root, file)).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal));

    /// <summary>
    /// Lines that can change a diagnostic's severity: C# without whole-line comments (a #pragma
    /// still counts), .editorconfig without '#' and ';' comments, MSBuild files whole.
    /// </summary>
    private static IEnumerable<string> SettingLines(string file) => Path.GetExtension(file) switch
    {
        ".cs" => CodeLines(file),
        ".editorconfig" or ".globalconfig" => File.ReadLines(Path.Combine(Root, file))
            .Where(line => !line.TrimStart().StartsWith('#') && !line.TrimStart().StartsWith(';')),
        _ => File.ReadLines(Path.Combine(Root, file)),
    };
}
