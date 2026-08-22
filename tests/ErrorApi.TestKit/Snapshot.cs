using System.Runtime.CompilerServices;
using Xunit;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// A dependency-free snapshot assertion. Approved output lives next to the tests as
/// <c>Snapshots/&lt;name&gt;.verified.txt</c>; a mismatch writes <c>.received.txt</c> beside it and
/// fails with the first differing line.
/// </summary>
/// <remarks>
/// Set <c>ERRORAPI_ACCEPT_SNAPSHOTS=1</c> to rewrite the approved files instead of failing. Review
/// the resulting diff like any other change — that diff is the point of these tests.
/// </remarks>
public static class Snapshot
{
    private const string AcceptVariable = "ERRORAPI_ACCEPT_SNAPSHOTS";

    /// <summary>Asserts that <paramref name="actual"/> matches the approved snapshot for <paramref name="name"/>.</summary>
    public static void Match(string actual, string name, [CallerFilePath] string callerPath = "")
    {
        var directory = Path.Combine(Path.GetDirectoryName(callerPath)!, "Snapshots");
        Directory.CreateDirectory(directory);

        var verifiedPath = Path.Combine(directory, name + ".verified.txt");
        var receivedPath = Path.Combine(directory, name + ".received.txt");

        var normalized = Normalize(actual);

        if (Environment.GetEnvironmentVariable(AcceptVariable) == "1")
        {
            File.WriteAllText(verifiedPath, normalized);
            File.Delete(receivedPath);
            return;
        }

        if (!File.Exists(verifiedPath))
        {
            File.WriteAllText(receivedPath, normalized);
            Assert.Fail(
                $"No approved snapshot '{name}'. The output was written to:\n  {receivedPath}\n" +
                $"Review it, then approve with {AcceptVariable}=1.");
        }

        var expected = Normalize(File.ReadAllText(verifiedPath));
        if (expected == normalized)
        {
            File.Delete(receivedPath);
            return;
        }

        File.WriteAllText(receivedPath, normalized);
        Assert.Fail($"Snapshot '{name}' does not match.\n{Describe(expected, normalized)}\nReceived output: {receivedPath}");
    }

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n").Replace("﻿", string.Empty).TrimEnd() + "\n";

    private static string Describe(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');

        for (var i = 0; i < Math.Max(expectedLines.Length, actualLines.Length); i++)
        {
            var left = i < expectedLines.Length ? expectedLines[i] : "<missing>";
            var right = i < actualLines.Length ? actualLines[i] : "<missing>";

            if (left != right)
            {
                return $"First difference on line {i + 1}:\n  expected: {left}\n  actual:   {right}";
            }
        }

        return "The files differ only in trailing whitespace.";
    }
}
