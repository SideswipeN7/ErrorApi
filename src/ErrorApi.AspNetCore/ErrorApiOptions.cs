using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace ErrorApi.AspNetCore;

/// <summary>
/// Configures <c>AddErrorApi(...)</c>. The host assembly's own generated model always comes first;
/// what these options add are the models of <em>other</em> assemblies — most importantly their
/// instance-type switches, which resolve failures whose types are declared there.
/// </summary>
public sealed class ErrorApiOptions
{
    internal List<IErrorApiMetadata> Included { get; } = [];

    /// <summary>
    /// Composes additional compile-time models into the registered one. Every assembly that runs the
    /// generator exposes its model as <c>&lt;AssemblyName&gt;.ErrorApiModel.Metadata</c>, so the layered
    /// shape reads exactly as it is:
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddErrorApi(x => x.Include(
    ///     MyProject.Domain.ErrorApiModel.Metadata,
    ///     MyProject.Application.ErrorApiModel.Metadata));
    /// </code>
    /// </example>
    /// <param name="models">The models to include, in priority order after the host's own.</param>
    public ErrorApiOptions Include(params IErrorApiMetadata[] models)
    {
        ArgumentNullException.ThrowIfNull(models);

        foreach (var model in models)
        {
            if (model is null)
            {
                throw new ArgumentException("A model must not be null.", nameof(models));
            }

            Included.Add(model);
        }

        return this;
    }

    /// <summary>
    /// The reflection convenience over <see cref="Include"/>: finds each assembly's generated
    /// <c>ErrorApiModel.Metadata</c> by its well-known name. Startup-time only — nothing here runs on
    /// the request path — but prefer <see cref="Include"/> where trimming or native AOT matters,
    /// because it references the model statically and cannot miss.
    /// </summary>
    /// <param name="assemblies">Assemblies that ran the ErrorApi generator.</param>
    /// <exception cref="InvalidOperationException">An assembly has no generated model.</exception>
    [RequiresUnreferencedCode("Resolves the generated ErrorApiModel type by name; use Include(...) under trimming or native AOT.")]
    public ErrorApiOptions IncludeFromAssemblies(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (var assembly in assemblies)
        {
            var name = SanitizeNamespace(assembly.GetName().Name ?? string.Empty);
            var accessor = assembly.GetType(name + ".ErrorApiModel");
            var metadata = accessor?.GetProperty("Metadata", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as IErrorApiMetadata;

            Included.Add(metadata ?? throw new InvalidOperationException(
                $"Assembly '{assembly.GetName().Name}' has no generated ErrorApi model. " +
                "It must reference the ErrorApi generator (any ErrorApi package brings it along)."));
        }

        return this;
    }

    /// <summary>
    /// The namespace the generator derives from an assembly name — kept in step with the emitter's
    /// twin, the same way the two <c>RoutePattern.Normalize</c> copies are.
    /// </summary>
    internal static string SanitizeNamespace(string assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
        {
            return "ErrorApiAssembly";
        }

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
}
