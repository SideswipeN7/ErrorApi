using ErrorApi.Generator.Helpers;
using Xunit;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// The generator normalizes routes at compile time and the runtime normalizes
/// <c>ApiDescription.RelativePath</c>; a lookup only works if both land on the same string. The
/// generator cannot reference the runtime assembly, so the transform exists twice — these tests are
/// what keeps the copies honest.
/// </summary>
public sealed class RouteNormalizationTests
{
    public static TheoryData<string, string> Patterns => new()
    {
        { "/orders", "/orders" },
        { "orders", "/orders" },
        { "/Orders/", "/orders" },
        { "//orders//", "/orders" },
        { "/orders/{id}", "/orders/{id}" },
        { "/orders/{id:guid}", "/orders/{id}" },
        { "/orders/{id:int:min(1)}", "/orders/{id}" },
        { "/orders/{id?}", "/orders/{id}" },
        { "/orders/{id=7}", "/orders/{id}" },
        { "/files/{*path}", "/files/{path}" },
        { "/files/{**slug}", "/files/{slug}" },
        { "/orders/{id:regex(^[0-9]+$)}", "/orders/{id}" },
        { "/orders/{id}/lines/{lineId:guid}", "/orders/{id}/lines/{lineId}" },
        { "", "/" },
        { "/", "/" },
    };

    [Theory]
    [MemberData(nameof(Patterns))]
    public void The_runtime_normalizer_produces_the_expected_shape(string pattern, string expected) =>
        Assert.Equal(expected, RoutePattern.Normalize(pattern));

    [Theory]
    [MemberData(nameof(Patterns))]
    public void The_generator_copy_agrees_with_the_runtime(string pattern, string expected) =>
        Assert.Equal(expected, RouteNormalizer.Normalize(pattern));

    [Theory]
    [InlineData("/orders", "/{id}", "/orders/{id}")]
    [InlineData("/orders", "/", "/orders")]
    [InlineData("", "/orders", "/orders")]
    [InlineData("/api/v1/", "/orders/{id:guid}", "/api/v1/orders/{id}")]
    public void Group_prefixes_combine_the_same_way_on_both_sides(string prefix, string pattern, string expected)
    {
        Assert.Equal(expected, RoutePattern.Combine(prefix, pattern));
        Assert.Equal(expected, RouteNormalizer.Combine(prefix, pattern));
    }
}
