namespace ErrorApi.Generator.Helpers;

/// <summary>
/// Emit-time face of <c>ErrorApi.EndpointGroup.Normalize</c>: the generated lookup switches on
/// normalized group names, and both sides compile the same linked source —
/// <c>src/Shared/SharedNormalization.cs</c> — so the case labels and the runtime comparison cannot drift.
/// </summary>
internal static class GroupNormalizer
{
    public static string? Normalize(string? group) => Shared.SharedNormalization.NormalizeGroup(group);
}
