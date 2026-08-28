using System.Text.RegularExpressions;

namespace RAG.Infrastructure.Files;

public static partial class TextSanitizer
{
    [GeneratedRegex("[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]")]
    private static partial Regex ControlChars();

    public static string Sanitize(string input)
    {
        var withoutControls = ControlChars().Replace(input, " ");
        return RemoveUnpairedSurrogates(withoutControls);
    }

    private static string RemoveUnpairedSurrogates(string input)
    {
        var chars = input.Where(c => !char.IsSurrogate(c) || char.IsHighSurrogate(c)).ToArray();
        var result = new string(chars);
        var sb = new System.Text.StringBuilder(result.Length);
        for (var i = 0; i < result.Length; i++)
        {
            var c = result[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < result.Length && char.IsLowSurrogate(result[i + 1]))
                {
                    sb.Append(c).Append(result[i + 1]);
                    i++;
                }
            }
            else if (!char.IsLowSurrogate(c))
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
