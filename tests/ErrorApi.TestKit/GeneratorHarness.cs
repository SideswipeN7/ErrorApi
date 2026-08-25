using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ErrorApi.Generator.Tests;

/// <summary>The outcome of one generator run, in the form the tests assert on.</summary>
/// <param name="Sources">Generated files, keyed by hint name and ordered by it.</param>
/// <param name="GeneratorDiagnostics">Diagnostics the generator reported.</param>
/// <param name="CompilationDiagnostics">Errors and warnings of the compilation after generation.</param>
public sealed record GeneratorOutput(
    ImmutableSortedDictionary<string, string> Sources,
    ImmutableArray<Diagnostic> GeneratorDiagnostics,
    ImmutableArray<Diagnostic> CompilationDiagnostics)
{
    /// <summary>The generated file with the given hint name.</summary>
    public string Source(string hintName) =>
        Sources.TryGetValue(hintName, out var source)
            ? source
            : throw new InvalidOperationException(
                $"No generated file named '{hintName}'. Generated: {string.Join(", ", Sources.Keys)}");

    /// <summary>Renders everything the run produced as one stable, diffable document.</summary>
    public string ToSnapshot()
    {
        var builder = new StringBuilder();

        foreach (var (hintName, source) in Sources)
        {
            builder.Append("//---------- ").Append(hintName).Append(" ----------\n");
            builder.Append(source.Replace("\r\n", "\n").TrimEnd()).Append("\n\n");
        }

        builder.Append("//---------- diagnostics ----------\n");
        if (GeneratorDiagnostics.IsEmpty)
        {
            builder.Append("(none)\n");
        }

        foreach (var diagnostic in GeneratorDiagnostics
                     .OrderBy(d => d.Id, StringComparer.Ordinal)
                     .ThenBy(d => d.Location.GetLineSpan().StartLinePosition.Line))
        {
            builder.Append(diagnostic.Id).Append(' ').Append(diagnostic.Severity.ToString().ToLowerInvariant())
                   .Append(": ").Append(diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture))
                   .Append('\n');
        }

        return builder.ToString();
    }
}

/// <summary>
/// Compiles snippets against the real ASP.NET Core and ErrorApi assemblies and runs the generator over
/// them. Using the live reference set means the tests exercise the same symbol shapes as a real build.
/// </summary>
public static class GeneratorHarness
{
    private static readonly ImmutableArray<MetadataReference> References = LoadReferences();

    private static readonly CSharpParseOptions ParseOptions =
        new(LanguageVersion.Latest, documentationMode: DocumentationMode.None);

    /// <summary>Runs the generator over <paramref name="sources"/>.</summary>
    public static GeneratorOutput Run(params string[] sources) => Run([], sources);

    /// <summary>Runs the generator over <paramref name="sources"/> with additional references in scope.</summary>
    public static GeneratorOutput Run(IReadOnlyList<MetadataReference> extraReferences, params string[] sources)
    {
        var trees = sources
            .Select((text, index) => CSharpSyntaxTree.ParseText(text, ParseOptions, path: $"Source{index}.cs"))
            .ToImmutableArray();

        var compilation = CSharpCompilation.Create(
            "ErrorApi.GeneratorTests.Subject",
            trees,
            References.AddRange(extraReferences),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver
            .Create([new ErrorApiGenerator().AsSourceGenerator()], parseOptions: ParseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);

        var result = driver.GetRunResult().Results.Single();

        var generated = result.GeneratedSources
            .ToImmutableSortedDictionary(
                source => source.HintName,
                source => source.SourceText.ToString().Replace("\r\n", "\n"),
                StringComparer.Ordinal);

        return new GeneratorOutput(
            generated,
            result.Diagnostics,
            updated.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning).ToImmutableArray());
    }

    /// <summary>
    /// Runs the generator, applies <paramref name="edit"/> to the compilation, runs again on the same
    /// driver, and hands back the second run with step tracking on — which is how a test watches what
    /// an edit did or did not invalidate.
    /// </summary>
    public static GeneratorRunResult RunTwice(string[] sources, Func<CSharpCompilation, CSharpCompilation> edit)
    {
        var trees = sources
            .Select((text, index) => CSharpSyntaxTree.ParseText(text, ParseOptions, path: $"Source{index}.cs"))
            .ToImmutableArray();

        var compilation = CSharpCompilation.Create(
            "ErrorApi.GeneratorTests.Subject",
            trees,
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = (GeneratorDriver)CSharpGeneratorDriver.Create(
            [new ErrorApiGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);
        driver = driver.RunGenerators(edit(compilation));

        return driver.GetRunResult().Results.Single();
    }

    /// <summary>Parses one more source with the harness's parse options, for use with <see cref="RunTwice"/>.</summary>
    public static Microsoft.CodeAnalysis.SyntaxTree ParseTree(string source, string path) =>
        CSharpSyntaxTree.ParseText(source, ParseOptions, path: path);

    /// <summary>
    /// Compiles <paramref name="sources"/> — with the generator applied, so exports and generated
    /// members are baked in — and hands the result back as a metadata reference. This is how a test
    /// stands in for a catalog shipped as a NuGet package: the consumer sees attributes and signatures,
    /// never bodies.
    /// </summary>
    public static MetadataReference CompileToReference(string assemblyName, params string[] sources)
    {
        var trees = sources
            .Select((text, index) => CSharpSyntaxTree.ParseText(text, ParseOptions, path: $"{assemblyName}{index}.cs"))
            .ToImmutableArray();

        var compilation = CSharpCompilation.Create(
            assemblyName,
            trees,
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        CSharpGeneratorDriver
            .Create([new ErrorApiGenerator().AsSourceGenerator()], parseOptions: ParseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);

        using var stream = new MemoryStream();
        var emit = updated.Emit(stream);
        if (!emit.Success)
        {
            throw new InvalidOperationException(
                "The referenced library does not compile:\n" +
                string.Join("\n", emit.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => $"  {d.Id}: {d.GetMessage()}")));
        }

        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    /// <summary>Runs the generator and fails unless the resulting compilation is error-free.</summary>
    public static GeneratorOutput RunAndCompile(params string[] sources)
    {
        var output = Run(sources);
        var errors = output.CompilationDiagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "The generated code does not compile:\n" +
                string.Join("\n", errors.Select(e => $"  {e.Id} {e.Location.GetLineSpan()}: {e.GetMessage()}")));
        }

        return output;
    }

    /// <summary>
    /// Uses the assemblies this test process already runs on, which is the simplest way to get a
    /// reference set that matches the shipping ASP.NET Core surface exactly.
    /// </summary>
    private static ImmutableArray<MetadataReference> LoadReferences()
    {
        var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;

        return trusted
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    }
}
