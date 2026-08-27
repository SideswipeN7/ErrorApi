using System;

namespace ErrorApi;

/// <summary>
/// Records which error codes are reachable from one member, written by the generator into the assembly
/// that implements the member. Not meant to be written by hand.
/// </summary>
/// <remarks>
/// <para>
/// The reachability walk reads source, so it stops at an assembly boundary: a service implemented in
/// your Application project is invisible to the Api project's generator. When the implementing project
/// runs the generator itself, it walks its own methods and handlers and bakes the result in as these
/// attributes; the consuming compilation reads them back through the reference and the walk continues
/// as if the boundary were not there. Exports compose transitively — each assembly's walk reads the
/// exports of the assemblies it references.
/// </para>
/// <para>
/// <see cref="MemberId"/> is a documentation comment ID: <c>M:…</c> for a method whose body raises or
/// returns the codes, <c>T:…</c> for a message type whose handlers do.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class ReachabilityExportAttribute : Attribute
{
    /// <param name="memberId">The documentation comment ID of the member or message type.</param>
    /// <param name="codes">The catalog codes reachable from it.</param>
    public ReachabilityExportAttribute(string memberId, params string[] codes)
    {
        MemberId = memberId;
        Codes = codes;
    }

    /// <summary>The documentation comment ID of the member or message type.</summary>
    public string MemberId { get; }

    /// <summary>The catalog codes reachable from the member.</summary>
    public string[] Codes { get; }
}
