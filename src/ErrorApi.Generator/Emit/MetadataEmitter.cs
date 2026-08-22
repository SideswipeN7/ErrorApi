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
        IReadOnlyList<CatalogEntry> errorTypes)
    {
        var index = new Dictionary<string, int>(System.StringComparer.Ordinal);
        for (var i = 0; i < errors.Count; i++)
        {
            index[errors[i].Code] = i;
        }

        var writer = new SourceWriter();
        GeneratedFileHeader.Write(writer);

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

        return writer.ToString();
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

                writer.Line(
                    $"new {EndpointErrors}({SourceWriter.Literal(endpoint.HttpMethod)}, " +
                    $"{SourceWriter.Literal(endpoint.RoutePattern)}, {payload}),");
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
        var signature = $"public bool TryGetEndpointErrors(string httpMethod, string routePattern, out {ReadOnlyList}<{Descriptor}> errors)";

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
                                foreach (var (endpoint, ordinal) in route)
                                {
                                    if (endpoint.HttpMethod == "*")
                                    {
                                        writer.Line($"default: errors = _endpoints[{ordinal}].Errors; return true;");
                                        continue;
                                    }

                                    foreach (var method in endpoint.HttpMethod.Split(','))
                                    {
                                        writer.Line($"case {SourceWriter.Literal(method)}: errors = _endpoints[{ordinal}].Errors; return true;");
                                    }
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
            }
        }

        return writer.ToString();
    }
}
