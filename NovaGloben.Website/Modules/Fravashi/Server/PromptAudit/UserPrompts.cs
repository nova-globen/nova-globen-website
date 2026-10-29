using System.Text;
using System.Text.Json;

namespace Fravashi.Broker.PromptAudit;

/// <summary>What a forwarded body holds that a PERSON wrote, or that it could not be read.</summary>
/// <param name="Readable">False when the body is not a request this broker knows how to read the prompts out of.</param>
/// <param name="Texts">Every user-authored text in it, one entry per user message (or per legacy prompt).</param>
public sealed record UserPromptRead(bool Readable, IReadOnlyList<string> Texts)
{
    public static readonly UserPromptRead Unreadable = new(false, []);
}

/// <summary>
/// Reads the user prompts out of a request body. "The user prompt" is taken as EVERY user message in
/// the request, not only the newest, and the reason is not thoroughness for its own sake. The agent CLI sends the
/// whole conversation every time, so a prompt that was refused stays in the history the next turn carries; judging
/// only the newest message would forward it on that next turn, uninspected. Earlier messages cost nothing to
/// re-check because judgements are remembered by the text's hash (<see cref="PromptAuditor"/>).
/// <para>
/// What is NOT read, on purpose: <c>tool_result</c> blocks (the output of a tool, a file's contents, a command's
/// output, which the user did not write and the rule does not cover), images, the <c>system</c> field
/// and assistant turns. A client other than the studio could put an instruction in <c>system</c> or forge an
/// assistant turn; the studio lets a person write neither, and the inspection records that limit rather than hiding
/// it. Text inside a <c>&lt;system-reminder&gt;</c> is NOT stripped even though the CLI writes it and not the
/// user, because a user can write those tags too, and stripping them would be the one-line way around.
/// </para>
/// <para>
/// Three body shapes are known: the Messages API (<c>messages</c>), a message batch (<c>requests[].params
/// .messages</c>), and the legacy completions API (<c>prompt</c>). Anything else is <see
/// cref="UserPromptRead.Unreadable"/> and is refused while inspection is on: a provider would reject such a
/// body anyway, so refusing costs nothing honest, and forwarding what could not be read would be the gap.
/// </para>
/// </summary>
public static class UserPrompts
{
    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 256 };

    public static UserPromptRead Read(byte[] body)
    {
        if (body is null || body.Length == 0)
        {
            return UserPromptRead.Unreadable;
        }

        try
        {
            using var doc = JsonDocument.Parse(body, Options);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return UserPromptRead.Unreadable;
            }

            var texts = new List<string>();
            var known = false;

            if (root.TryGetProperty("messages", out var messages))
            {
                if (!ReadMessages(messages, texts))
                {
                    return UserPromptRead.Unreadable;
                }

                known = true;
            }

            if (root.TryGetProperty("requests", out var requests))
            {
                if (requests.ValueKind != JsonValueKind.Array)
                {
                    return UserPromptRead.Unreadable;
                }

                foreach (var request in requests.EnumerateArray())
                {
                    if (request.ValueKind != JsonValueKind.Object
                        || !request.TryGetProperty("params", out var parameters)
                        || parameters.ValueKind != JsonValueKind.Object
                        || !parameters.TryGetProperty("messages", out var batchMessages)
                        || !ReadMessages(batchMessages, texts))
                    {
                        return UserPromptRead.Unreadable;
                    }
                }

                known = true;
            }

            if (root.TryGetProperty("prompt", out var prompt))
            {
                if (prompt.ValueKind != JsonValueKind.String)
                {
                    return UserPromptRead.Unreadable;
                }

                Add(texts, prompt.GetString());
                known = true;
            }

            return known ? new UserPromptRead(true, texts) : UserPromptRead.Unreadable;
        }
        catch (JsonException)
        {
            return UserPromptRead.Unreadable;
        }
    }

    private static bool ReadMessages(JsonElement messages, List<string> texts)
    {
        if (messages.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var message in messages.EnumerateArray())
        {
            if (message.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            // A role that is not a string is a body the provider would refuse; not reading it is the safe side.
            if (!message.TryGetProperty("role", out var role) || role.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            if (!string.Equals(role.GetString(), "user", StringComparison.Ordinal))
            {
                continue;
            }

            if (!message.TryGetProperty("content", out var content))
            {
                return false;
            }

            switch (content.ValueKind)
            {
                case JsonValueKind.String:
                    Add(texts, content.GetString());
                    break;
                case JsonValueKind.Array:
                    Add(texts, JoinBlocks(content));
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    /// <summary>The user-written text of one message's blocks, joined, so a message is judged as one prompt.</summary>
    private static string JoinBlocks(JsonElement blocks)
    {
        var joined = new StringBuilder();
        foreach (var block in blocks.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var type = block.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            if (type == "tool_result")
            {
                continue;
            }

            string? text = null;
            if (block.TryGetProperty("text", out var value) && value.ValueKind == JsonValueKind.String)
            {
                // Any block that carries a text field, whatever its type, so a block type nobody here has heard of
                // is read rather than skipped.
                text = value.GetString();
            }
            else if (type == "document"
                     && block.TryGetProperty("source", out var source)
                     && source.ValueKind == JsonValueKind.Object
                     && source.TryGetProperty("type", out var sourceType)
                     && sourceType.ValueKind == JsonValueKind.String
                     && sourceType.GetString() == "text"
                     && source.TryGetProperty("data", out var data)
                     && data.ValueKind == JsonValueKind.String)
            {
                // A plain-text document the user attached is words the user put in front of the model.
                text = data.GetString();
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                if (joined.Length > 0)
                {
                    joined.Append("\n\n");
                }

                joined.Append(text);
            }
        }

        return joined.ToString();
    }

    private static void Add(List<string> texts, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            texts.Add(text);
        }
    }
}
