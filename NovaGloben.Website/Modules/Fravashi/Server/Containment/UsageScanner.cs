using System.Text;
using System.Text.RegularExpressions;

namespace Fravashi.Broker.Containment;

/// <summary>
/// Reads the provider's OWN usage figures out of an answer as it streams past, without buffering
/// the answer and without reading what it says: it looks for four field names followed by a number and nothing
/// else. It is fed the same bytes the client is being sent, so a streamed answer stays streamed.
/// <para>
/// Each figure is kept as the LARGEST value seen, never a sum. A streamed answer reports the input side once
/// and then the CUMULATIVE output on every delta, so adding the events up would charge the output several times
/// over; the final figure is the largest one. A number cut in half by a chunk boundary is read whole, because
/// the tail of each chunk is carried into the next and a figure that runs to the very end of what has arrived
/// is left for the next chunk to complete.
/// </para>
/// </summary>
public sealed partial class UsageScanner
{
    private const int Carry = 96;

    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly Dictionary<string, long> _largest = new(StringComparer.Ordinal);
    private string _tail = string.Empty;

    /// <summary>
    /// What the provider says this answer used: input, cache writes, cache reads and output together. Null when
    /// no figure was seen at all, which is NOT zero: the caller charges a stated worst case for it. Reading this
    /// settles a figure still waiting at the very end of the answer, so read it once the answer is over.
    /// </summary>
    public long? Tokens
    {
        get
        {
            Scan(_tail + " ", holdBackTheEnd: false);
            return _largest.Count == 0 ? null : _largest.Values.Sum();
        }
    }

    /// <summary>
    /// The same figures one by one (a figure never seen is 0), for weighting; null when none was seen at all. Read
    /// once the answer is over, as <see cref="Tokens"/>.
    /// </summary>
    public TokenUsage? Usage
    {
        get
        {
            Scan(_tail + " ", holdBackTheEnd: false);
            if (_largest.Count == 0)
            {
                return null;
            }
            long Of(string name) => _largest.TryGetValue(name, out var value) ? value : 0;
            return new TokenUsage(Of("input_tokens"), Of("cache_creation_input_tokens"), Of("cache_read_input_tokens"), Of("output_tokens"));
        }
    }

    /// <summary>Looks at the next bytes of the answer.</summary>
    public void Feed(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return;
        }

        var chars = new char[_decoder.GetCharCount(bytes, flush: false)];
        var written = _decoder.GetChars(bytes, chars, flush: false);
        var text = _tail + new string(chars, 0, written);

        Scan(text, holdBackTheEnd: true);
        _tail = text.Length <= Carry ? text : text[^Carry..];
    }

    private void Scan(string text, bool holdBackTheEnd)
    {
        foreach (Match match in Figure().Matches(text))
        {
            // A figure that runs to the very end of what has arrived may be the first half of a longer number.
            if (holdBackTheEnd && match.Index + match.Length == text.Length)
            {
                continue;
            }

            if (long.TryParse(match.Groups["n"].ValueSpan, out var value)
                && (!_largest.TryGetValue(match.Groups["f"].Value, out var seen) || value > seen))
            {
                _largest[match.Groups["f"].Value] = value;
            }
        }
    }

    [GeneratedRegex(@"""(?<f>input_tokens|output_tokens|cache_creation_input_tokens|cache_read_input_tokens)""\s*:\s*(?<n>\d{1,15})")]
    private static partial Regex Figure();
}
