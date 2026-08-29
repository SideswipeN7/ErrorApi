using System.Collections.Generic;
using System.Linq;
using ErrorApi.Generator.Helpers;
using ErrorApi.Generator.Model;

namespace ErrorApi.Generator.Emit;

/// <summary>
/// Writes the compile-time error model: the descriptor table, the endpoint-to-errors map, and the
/// <c>IErrorApiMetadata</c> implementation that the OpenAPI transformer reads. Every lookup is a
/// <see langword="switch"/> over string constants, so nothing here needs reflection or a dictionary
/// built at startup.
/// </summary>
internal static class MetadataEmitter
{
    public const string HintName = "ErrorApi.Metadata.g.cs";
    public const string RegistrationHintName = "ErrorApi.Registration.g.cs";

    private const string Descriptor = "global::ErrorApi.ErrorDescriptor";
    private const string EndpointErrors = "global::ErrorApi.EndpointErrors";
    private const string ReadOnlyList = "global::System.Collections.Generic.IReadOnlyList";

    public static string Emit(
        IReadOnlyList<DiscoveredError> errors,
        IReadOnlyList<EndpointModel> endpoints,
        IReadOnlyList<CatalogEntry> errorTypes,
        IReadOnlyList<ReachabilityExport> reachability,
        string assemblyName = "")
    {
        var index = new Dictionary<string, int>(System.StringComparer.Ordinal);
        for (var i = 0; i < errors.Count; i++)
        {
            index[errors[i].Code] = i;
        }

        var writer = new SourceWriter();
        GeneratedFileHeader.Write(writer);

        // Body-inferred codes cannot be re-derived from metadata by a consuming compilation, so the
        // resolution is baked into the assembly and read back through the reference.
        foreach (var entry in errorTypes.Where(e => e.ExportId is not null).OrderBy(e => e.Code, System.StringComparer.Ordinal))
        {
            writer.Line($"[assembly: global::ErrorApi.CatalogExport({SourceWriter.Literal(entry.ExportId)}, {SourceWriter.Literal(entry.Code)})]");
        }

        // A library's walk starts at its own public surface; what each member can reach is baked in so
        // a consuming compilation can continue the walk across the assembly boundary.
        foreach (var export in reachability)
        {
            var codes = string.Join(", ", export.Codes.Select(c => SourceWriter.Literal(c)));
            writer.Line($"[assembly: global::ErrorApi.ReachabilityExport({SourceWriter.Literal(export.MemberId)}, {codes})]");
        }

        using (writer.Block("namespace ErrorApi.Generated"))
        {
            writer.Line("/// <summary>The error catalog and endpoint contract of this assembly, as seen by the compiler.</summary>");
            using (writer.Block("internal static class ErrorApiGenerated"))
            {
                WriteErrorTable(writer, errors);
                writer.Line();
                WriteEndpointTable(writer, endpoints, index);
                writer.Line();
                writer.Line("/// <summary>The compile-time error model of this assembly.</summary>");
                writer.Line("public static global::ErrorApi.IErrorApiMetadata Metadata { get; } = new Model();");
                writer.Line();
                WriteModel(writer, errors, endpoints, errorTypes, index);
            }
        }

        // The public face of this assembly's model, under a namespace derived from the assembly name so
        // two referenced assemblies never collide. This is what AddErrorApi(x => x.Include(...)) and
        // IncludeFromAssemblies resolve — a consumer composes referenced models by writing
        // `Other.Assembly.ErrorApiModel.Metadata`, statically and AOT-clean.
        if (assemblyName.Length > 0)
        {
            writer.Line();
            using (writer.Block($"namespace {SanitizeNamespace(assemblyName)}"))
            {
                writer.Line("/// <summary>This assembly's compile-time ErrorApi model, for composition by a consumer.</summary>");
                using (writer.Block("public static class ErrorApiModel"))
                {
                    writer.Line("/// <summary>The model, as generated for this assembly.</summary>");
                    writer.Line("public static global::ErrorApi.IErrorApiMetadata Metadata => global::ErrorApi.Generated.ErrorApiGenerated.Metadata;");
                }
            }
        }

        return writer.ToString();
    }

    /// <summary>
    /// The namespace derived from an assembly name. Kept in step with its runtime twin,
    /// <c>ErrorApiOptions.SanitizeNamespace</c>, the same way the two <c>RoutePattern.Normalize</c>
    /// copies are — change one, change both.
    /// </summary>
    private static string SanitizeNamespace(string assemblyName)
    {
        var builder = new System.Text.StringBuilder(assemblyName.Length);
        var startOfSegment = true;

        foreach (var c in assemblyName)
        {
            if (c == '.')
            {
                builder.Append('.');
                startOfSegment = true;
                continue;
            }

            var valid = char.IsLetterOrDigit(c) || c == '_' ? c : '_';
            if (startOfSegment && char.IsDigit(valid))
            {
                builder.Append('_');
            }

            builder.Append(valid);
            startOfSegment = false;
        }

        return builder.ToString();
    }

    private static void WriteErrorTable(SourceWriter writer, IReadOnlyList<DiscoveredError> errors)
    {
        if (errors.Count == 0)
        {
            writer.Line($"private static readonly {Descriptor}[] _errors = global::System.Array.Empty<{Descriptor}>();");
            return;
        }

        using (writer.Block($"private static readonly {Descriptor}[] _errors =", ";"))
        {
            foreach (var error in errors)
            {
                writer.Line(
                    $"new {Descriptor}({SourceWriter.Literal(error.Code)}, {error.StatusCode}, " +
                    $"{SourceWriter.Literal(error.Title)}, {SourceWriter.Literal(error.Detail)}, " +
                    $"{SourceWriter.Literal(error.Description)}, {SourceWriter.Literal(error.DeclaringMember)}),");
            }
        }
    }

    private static void WriteEndpointTable(SourceWriter writer, IReadOnlyList<EndpointModel> endpoints, IReadOnlyDictionary<string, int> index)
    {
        if (endpoints.Count == 0)
        {
            writer.Line($"private static readonly {EndpointErrors}[] _endpoints = global::System.Array.Empty<{EndpointErrors}>();");
            return;
        }

        using (writer.Block($"private static readonly {EndpointErrors}[] _endpoints =", ";"))
        {
            foreach (var endpoint in endpoints)
            {
                var references = endpoint.ErrorCodes
                    .Where(index.ContainsKey)
                    .Select(code => $"_errors[{index[code]}]")
                    .ToList();

                var payload = references.Count == 0
                    ? $"global::System.Array.Empty<{Descriptor}>()"
                    : $"new {Descriptor}[] {{ {string.Join(", ", references)} }}";

                var group = endpoint.Group is null ? string.Empty : $", {SourceWriter.Literal(endpoint.Group)}";
                writer.Line(
                    $"new {EndpointErrors}({SourceWriter.Literal(endpoint.HttpMethod)}, " +
                    $"{SourceWriter.Literal(endpoint.RoutePattern)}, {payload}{group}),");
            }
        }
    }

    private static void WriteModel(
        SourceWriter writer,
        IReadOnlyList<DiscoveredError> errors,
        IReadOnlyList<EndpointModel> endpoints,
        IReadOnlyList<CatalogEntry> errorTypes,
        IReadOnlyDictionary<string, int> index)
    {
        using (writer.Block("private sealed class Model : global::ErrorApi.IErrorApiMetadata"))
        {
            writer.Line($"public {ReadOnlyList}<{Descriptor}> AllErrors => _errors;");
            writer.Line();
            writer.Line($"public {ReadOnlyList}<{EndpointErrors}> Endpoints => _endpoints;");
            writer.Line();

            WriteFindError(writer, errors);
            writer.Line();
            WriteFindErrorForInstance(writer, errorTypes, index);
            writer.Line();
            WriteTryGetEndpointErrors(writer, endpoints);
        }
    }

    private static void WriteFindError(SourceWriter writer, IReadOnlyList<DiscoveredError> errors)
    {
        if (errors.Count == 0)
        {
            writer.Line($"public {Descriptor}? FindError(string code) => null;");
            return;
        }

        using (writer.Block($"public {Descriptor}? FindError(string code) => code switch", ";"))
        {
            for (var i = 0; i < errors.Count; i++)
            {
                writer.Line($"{SourceWriter.Literal(errors[i].Code)} => _errors[{i}],");
            }

            writer.Line("_ => null,");
        }
    }

    /// <summary>
    /// Writes the instance-to-entry switch. Pattern matching on the annotated types keeps the lookup
    /// free of reflection, which is what makes the adapter packages work under native AOT.
    /// </summary>
    private static void WriteFindErrorForInstance(
        SourceWriter writer, IReadOnlyList<CatalogEntry> errorTypes, IReadOnlyDictionary<string, int> index)
    {
        var known = errorTypes
            .Where(e => e.ErrorTypeDisplay is not null && index.ContainsKey(e.Code))
            .OrderBy(e => e.ErrorTypeDisplay, System.StringComparer.Ordinal)
            .ToList();

        if (known.Count == 0)
        {
            writer.Line($"public {Descriptor}? FindErrorForInstance(object? instance) => null;");
            return;
        }

        using (writer.Block($"public {Descriptor}? FindErrorForInstance(object? instance) => instance switch", ";"))
        {
            foreach (var entry in known)
            {
                writer.Line($"{entry.ErrorTypeDisplay} => _errors[{index[entry.Code]}],");
            }

            writer.Line("_ => null,");
        }
    }

    private static void WriteTryGetEndpointErrors(SourceWriter writer, IReadOnlyList<EndpointModel> endpoints)
    {
        writer.Line($"public bool TryGetEndpointErrors(string httpMethod, string routePattern, out {ReadOnlyList}<{Descriptor}> errors)");
        writer.Line("    => TryGetEndpointErrors(httpMethod, routePattern, null, out errors);");
        writer.Line();

        var signature = $"public bool TryGetEndpointErrors(string httpMethod, string routePattern, string? group, out {ReadOnlyList}<{Descriptor}> errors)";

        using (writer.Block(signature))
        {
            if (endpoints.Count > 0)
            {
                using (writer.Block("switch (routePattern)"))
                {
                    var byRoute = endpoints
                        .Select((endpoint, ordinal) => (endpoint, ordinal))
                        .GroupBy(x => x.endpoint.RoutePattern, System.StringComparer.Ordinal)
                        .OrderBy(g => g.Key, System.StringComparer.Ordinal);

                    foreach (var route in byRoute)
                    {
                        writer.Line($"case {SourceWriter.Literal(route.Key)}:");
                        using (writer.Indented())
                        {
                            using (writer.Block("switch (httpMethod)"))
                            {
                                foreach (var method in MethodCases(route))
                                {
                                    WriteMethodCase(writer, method.Label, method.Entries);
                                }
                            }

                            writer.Line("break;");
                        }
                    }
                }

                writer.Line();
            }

            writer.Line($"errors = global::System.Array.Empty<{Descriptor}>();");
            writer.Line("return false;");
        }
    }

    /// <summary>One <c>httpMethod</c> switch label per verb, with every group's entry gathered under it.</summary>
    private static IEnumerable<(string Label, List<(string? Group, int Ordinal)> Entries)> MethodCases(
        IEnumerable<(EndpointModel endpoint, int ordinal)> route)
    {
        var byLabel = new Dictionary<string, List<(string?, int)>>(System.StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var (endpoint, ordinal) in route)
        {
            IEnumerable<string> labels = endpoint.HttpMethod == "*"
                ? new[] { "default:" }
                : endpoint.HttpMethod.Split(',').Select(m => $"case {SourceWriter.Literal(m)}:");

            foreach (var label in labels)
            {
                if (!byLabel.TryGetValue(label, out var entries))
                {
                    byLabel[label] = entries = [];
                    order.Add(label);
                }

                entries.Add((endpoint.Group, ordinal));
            }
        }

        foreach (var label in order)
        {
            yield return (label, byLabel[label]);
        }
    }

    /// <summary>
    /// Writes one verb's resolution. The rules mirror the documented contract: the exact group first,
    /// then the ungrouped entry, and a null group also matches a route that lives in exactly one group —
    /// so a purely cosmetic <c>WithGroupName</c> never hides an endpoint's errors.
    /// </summary>
    private static void WriteMethodCase(SourceWriter writer, string label, List<(string? Group, int Ordinal)> entries)
    {
        var ungroupedList = entries.Where(e => e.Group is null).ToList();
        int? ungrouped = ungroupedList.Count > 0 ? ungroupedList[0].Ordinal : null;
        var grouped = entries.Where(e => e.Group is not null).ToList();

        if (grouped.Count == 0)
        {
            // The common shape: one ungrouped endpoint, any group resolves to it.
            writer.Line($"{label} errors = _endpoints[{ungrouped}].Errors; return true;");
            return;
        }

        writer.Line(label);
        using (writer.Indented())
        {
            using (writer.Block("switch (group)"))
            {
                foreach (var (name, ordinal) in grouped)
                {
                    writer.Line($"case {SourceWriter.Literal(name)}: errors = _endpoints[{ordinal}].Errors; return true;");
                }
            }

            if (ungrouped is not null)
            {
                writer.Line($"errors = _endpoints[{ungrouped}].Errors; return true;");
            }
            else if (grouped.Count == 1)
            {
                using (writer.Block("if (group is null)"))
                {
                    writer.Line($"errors = _endpoints[{grouped[0].Ordinal}].Errors; return true;");
                }

                writer.Line("break;");
            }
            else
            {
                // Several groups and no ungrouped entry: a null group is ambiguous here, so it misses.
                writer.Line("break;");
            }
        }
    }

    /// <summary>
    /// Writes the zero-argument <c>AddErrorApi()</c> overload. It is only emitted when the compilation
    /// actually references ErrorApi.AspNetCore, so a class library holding just the catalog still builds.
    /// </summary>
    public static string EmitRegistration()
    {
        var writer = new SourceWriter();
        GeneratedFileHeader.Write(writer);

        using (writer.Block("namespace Microsoft.Extensions.DependencyInjection"))
        {
            writer.Line("/// <summary>Wires this assembly's generated error model into the container.</summary>");
            using (writer.Block("internal static class ErrorApiGeneratedServiceCollectionExtensions"))
            {
                writer.Line("/// <summary>Registers the compile-time error catalog and endpoint contract of this assembly.</summary>");
                writer.Line("public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddErrorApi(");
                writer.Line("    this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
                writer.Line("    => global::ErrorApi.AspNetCore.ErrorApiRegistration.Register(services, global::ErrorApi.Generated.ErrorApiGenerated.Metadata);");
                writer.Line();
                writer.Line("/// <summary>The configurable form: compose the models of referenced assemblies with <c>x.Include(...)</c>.</summary>");
                writer.Line("public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddErrorApi(");
                writer.Line("    this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services,");
                writer.Line("    global::System.Action<global::ErrorApi.AspNetCore.ErrorApiOptions> configure)");
                writer.Line("    => global::ErrorApi.AspNetCore.ErrorApiRegistration.Register(services, global::ErrorApi.Generated.ErrorApiGenerated.Metadata, configure);");
            }
        }

        return writer.ToString();
    }
}
