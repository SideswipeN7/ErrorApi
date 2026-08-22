using System;
using System.Text;

namespace ErrorApi.Generator.Helpers;

/// <summary>A minimal indenting writer; generated files are hand-shaped, not syntax-tree formatted.</summary>
internal sealed class SourceWriter
{
    private readonly StringBuilder _builder = new();
    private int _indent;

    public SourceWriter Line()
    {
        _builder.Append('\n');
        return this;
    }

    public SourceWriter Line(string text)
    {
        if (text.Length > 0)
        {
            _builder.Append(' ', _indent * 4).Append(text);
        }

        _builder.Append('\n');
        return this;
    }

    public IDisposable Block(string header)
    {
        Line(header);
        Line("{");
        _indent++;
        return new Closer(this, "}");
    }

    public IDisposable Block(string header, string suffix)
    {
        Line(header);
        Line("{");
        _indent++;
        return new Closer(this, "}" + suffix);
    }

    /// <summary>Indents without opening a brace, for the body of a <c>case</c> label.</summary>
    public IDisposable Indented()
    {
        _indent++;
        return new Closer(this, null);
    }

    public override string ToString() => _builder.ToString();

    /// <summary>Renders a C# string literal, or <c>null</c> for a missing value.</summary>
    public static string Literal(string? value) =>
        value is null ? "null" : "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\r").Replace("\n", "\n") + "\"";

    private sealed class Closer(SourceWriter writer, string? text) : IDisposable
    {
        public void Dispose()
        {
            writer._indent--;
            if (text is not null)
            {
                writer.Line(text);
            }
        }
    }
}
