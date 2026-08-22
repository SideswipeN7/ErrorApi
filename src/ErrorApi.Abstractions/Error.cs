using System;
using System.Collections.Generic;

namespace ErrorApi;

/// <summary>
/// A single failure produced by application code. Instances are normally not written by hand:
/// they are emitted by the ErrorApi source generator from a <see cref="ErrorAttribute"/>-annotated catalog.
/// </summary>
public readonly struct Error : IEquatable<Error>
{
    /// <summary>The absence of an error. <see cref="IsNone"/> is <see langword="true"/>.</summary>
    public static readonly Error None = default;

    /// <summary>Creates an error.</summary>
    /// <param name="code">Stable machine-readable code, e.g. <c>Orders.NotFound</c>.</param>
    /// <param name="statusCode">HTTP status code this error maps to.</param>
    /// <param name="title">Short human-readable summary, surfaced as <c>ProblemDetails.title</c>.</param>
    /// <param name="detail">Instance-specific explanation, surfaced as <c>ProblemDetails.detail</c>.</param>
    /// <param name="extensions">Additional members copied onto <c>ProblemDetails.extensions</c>.</param>
    public Error(string code, int statusCode, string? title = null, string? detail = null, IReadOnlyDictionary<string, object?>? extensions = null)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        StatusCode = statusCode;
        Title = title;
        Detail = detail;
        Extensions = extensions;
    }

    /// <summary>Stable machine-readable code, e.g. <c>Orders.NotFound</c>.</summary>
    public string Code { get; }

    /// <summary>HTTP status code this error maps to.</summary>
    public int StatusCode { get; }

    /// <summary>Short human-readable summary.</summary>
    public string? Title { get; }

    /// <summary>Instance-specific explanation.</summary>
    public string? Detail { get; }

    /// <summary>Additional members copied onto the emitted <c>ProblemDetails</c>.</summary>
    public IReadOnlyDictionary<string, object?>? Extensions { get; }

    /// <summary><see langword="true"/> when this is the default, error-free value.</summary>
    public bool IsNone => Code is null;

    /// <summary>Returns a copy carrying an instance-specific <paramref name="detail"/>.</summary>
    public Error WithDetail(string detail) => new(Code, StatusCode, Title, detail, Extensions);

    /// <summary>Returns a copy carrying a different <paramref name="title"/>.</summary>
    public Error WithTitle(string title) => new(Code, StatusCode, title, Detail, Extensions);

    /// <summary>Returns a copy with one extra <c>ProblemDetails</c> extension member.</summary>
    public Error WithExtension(string key, object? value)
    {
        var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (Extensions is not null)
        {
            foreach (var pair in Extensions)
            {
                copy[pair.Key] = pair.Value;
            }
        }

        copy[key] = value;
        return new Error(Code, StatusCode, Title, Detail, copy);
    }

    /// <inheritdoc />
    public bool Equals(Error other) =>
        string.Equals(Code, other.Code, StringComparison.Ordinal)
        && StatusCode == other.StatusCode
        && string.Equals(Title, other.Title, StringComparison.Ordinal)
        && string.Equals(Detail, other.Detail, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Error other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = Code is null ? 0 : StringComparer.Ordinal.GetHashCode(Code);
            hash = (hash * 397) ^ StatusCode;
            hash = (hash * 397) ^ (Detail is null ? 0 : StringComparer.Ordinal.GetHashCode(Detail));
            return hash;
        }
    }

    /// <inheritdoc />
    public override string ToString() => IsNone ? "Error.None" : $"{Code} ({StatusCode})";

    public static bool operator ==(Error left, Error right) => left.Equals(right);

    public static bool operator !=(Error left, Error right) => !left.Equals(right);
}
