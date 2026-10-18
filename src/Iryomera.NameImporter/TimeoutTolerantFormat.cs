using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;

namespace Iryomera.NameImporter;

/// <summary>
/// JsonSchema.Net checks <c>date-time</c> with a regex that has a 250 ms match timeout. The pattern is linear, but on a
/// workspace of a few hundred megabytes a garbage collection pause during one match can exceed it, so a valid workspace
/// failed to load at random. This wrapper keeps the original check and its result and repeats it after a timeout.
/// </summary>
internal sealed class TimeoutTolerantFormat(Format inner, int attempts = 5) : Format(inner.Key)
{
    private static readonly Lock Gate = new();

    public Format Inner { get; } = inner;

    public override bool Validate(JsonNode? node, out string? errorMessage)
    {
        for (var attempt = 1; ; attempt++)
            try { return Inner.Validate(node, out errorMessage); }
            catch (RegexMatchTimeoutException) when (attempt < attempts) { }
    }

    /// <summary>
    /// Replaces the format in JsonSchema.Net's process-wide registry. Formats are bound when a schema is parsed, so call this
    /// before parsing the schemas that use it.
    /// </summary>
    internal static void Install(Format format)
    {
        lock (Gate)
            if (Formats.Get(format.Key) is not TimeoutTolerantFormat) Formats.Register(new TimeoutTolerantFormat(format));
    }
}
