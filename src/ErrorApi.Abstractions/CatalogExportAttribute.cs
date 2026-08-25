using System;

namespace ErrorApi;

/// <summary>
/// Records the resolved wire code of one catalog member, written by the generator into the assembly
/// that declares the member. Not meant to be written by hand.
/// </summary>
/// <remarks>
/// A code inferred from a member's <em>body</em> — rule 2 of code inference — cannot be re-derived by a
/// consuming compilation, because a referenced assembly has no bodies to read. Without this record the
/// consumer would quietly fall back to name inference and document a different code than the one the
/// declaring assembly puts on the wire. The generator emits one of these per body-inferred entry, and
/// reads them back when it meets the member through a reference, so the declaring assembly's resolution
/// stays authoritative everywhere.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class CatalogExportAttribute : Attribute
{
    /// <param name="memberId">The documentation comment ID of the declaring member.</param>
    /// <param name="code">The wire code the declaring assembly resolved for it.</param>
    public CatalogExportAttribute(string memberId, string code)
    {
        MemberId = memberId;
        Code = code;
    }

    /// <summary>The documentation comment ID of the declaring member, e.g. <c>P:Shop.OrderErrors.Gone</c>.</summary>
    public string MemberId { get; }

    /// <summary>The wire code the declaring assembly resolved for the member.</summary>
    public string Code { get; }
}
