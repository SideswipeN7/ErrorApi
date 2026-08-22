using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ErrorApi.Generator.Helpers;

/// <summary>
/// A <see cref="Location"/> reduced to equatable data, so diagnostics can travel through the
/// incremental pipeline without pinning syntax nodes alive.
/// </summary>
internal sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);

    public static LocationInfo? From(SyntaxNode? node)
    {
        if (node is null)
        {
            return null;
        }

        var location = node.GetLocation();
        return location.SourceTree is null
            ? null
            : new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }
}
