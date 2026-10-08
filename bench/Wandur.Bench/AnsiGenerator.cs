using System.Text;

namespace Wandur.Bench;

/// <summary>
/// Deterministic, original MUD-like output: room text, colored channel chatter, combat spam with 16, 256 and
/// truecolor SGR, prompts and the odd long line. The same seed always produces the same text. Without <c>chat</c> the
/// channel lines become room speech, which the Channels panel does not pick up.
/// </summary>
public sealed class AnsiGenerator(int seed = 42, bool chat = true)
{
    private readonly Random _random = new(seed);
    private static readonly string[] Words =
    [
        "lantern", "river", "stone", "quiet", "forest", "tower", "ember", "glass", "harbor", "willow", "copper", "north",
        "market", "shadow", "silver", "path", "gate", "bridge", "orchard", "mist", "valley", "cart", "rope", "anvil",
        "the", "a", "of", "and", "to", "under", "beside", "toward", "old", "narrow", "bright", "cold", "warm", "small"
    ];
    private static readonly string[] Channels = ["gossip", "ooc", "newbie", "trade", "clan"];
    private static readonly string[] Names = ["Talek", "Ilsa", "Morrow", "Benn", "Quill", "Ardo", "Vesk"];
    private int _line;

    /// <summary>One line, ending in CR LF as most servers send it.</summary>
    public string NextLine()
    {
        _line++;
        var kind = _random.Next(100);
        var sb = new StringBuilder(160);
        if (kind < 30)
            sb.Append("\u001b[0;37m").Append(Sentence(8, 16)).Append("\u001b[0m");
        else if (kind < 45 && !chat)
            sb.Append("\u001b[0;37m").Append(Names[_random.Next(Names.Length)]).Append(" says, '").Append(Sentence(4, 12)).Append("'\u001b[0m");
        else if (kind < 45)
            sb.Append("\u001b[1;36m[").Append(Channels[_random.Next(Channels.Length)]).Append("]\u001b[0m \u001b[33m")
                .Append(Names[_random.Next(Names.Length)]).Append("\u001b[0m: ").Append(Sentence(4, 12));
        else if (kind < 70)
            sb.Append("\u001b[1;31m").Append(Names[_random.Next(Names.Length)]).Append("\u001b[0m hits you with ")
                .Append("\u001b[38;5;").Append(_random.Next(16, 256)).Append('m').Append(Word()).Append("\u001b[0m for \u001b[1;33m")
                .Append(_random.Next(1, 400)).Append("\u001b[0m damage. ").Append("\u001b[38;2;")
                .Append(_random.Next(256)).Append(';').Append(_random.Next(256)).Append(';').Append(_random.Next(256)).Append('m')
                .Append(Sentence(2, 5)).Append("\u001b[0m");
        else if (kind < 85)
            sb.Append("\u001b[32m<").Append(_random.Next(100, 999)).Append("hp ").Append(_random.Next(10, 300)).Append("m ")
                .Append(_random.Next(10, 300)).Append("mv>\u001b[0m ").Append(Sentence(1, 4));
        else if (kind < 97)
            sb.Append("\u001b[1;32m").Append(Capital(Sentence(2, 4))).Append("\u001b[0m\r\n\u001b[0;37m").Append(Sentence(14, 24))
                .Append("\u001b[0m\r\n\u001b[36mExits: north east south.\u001b[0m");
        else
            sb.Append("\u001b[35m").Append(Sentence(40, 60)).Append("\u001b[0m");
        sb.Append("\r\n");
        return sb.ToString();
    }

    /// <summary>Generated lines concatenated until at least <paramref name="characters"/> long.</summary>
    public string Block(int characters)
    {
        var sb = new StringBuilder(characters + 512);
        while (sb.Length < characters) sb.Append(NextLine());
        return sb.ToString();
    }

    /// <summary>Exactly <paramref name="count"/> display lines worth of text (multi-line entries count once per CR LF).</summary>
    public List<string> Chunks(int lines, int chunkCharacters)
    {
        var chunks = new List<string>();
        var sb = new StringBuilder(chunkCharacters + 512);
        var produced = 0;
        while (produced < lines)
        {
            var line = NextLine();
            produced += CountNewlines(line);
            sb.Append(line);
            if (sb.Length >= chunkCharacters) { chunks.Add(sb.ToString()); sb.Clear(); }
        }
        if (sb.Length > 0) chunks.Add(sb.ToString());
        return chunks;
    }

    private static int CountNewlines(string text) { var n = 0; foreach (var c in text) if (c == '\n') n++; return n; }
    private string Word() => Words[_random.Next(Words.Length)];
    private string Sentence(int min, int max)
    {
        var count = _random.Next(min, max + 1);
        var sb = new StringBuilder();
        for (var i = 0; i < count; i++) { if (i > 0) sb.Append(' '); sb.Append(Word()); }
        return sb.Append('.').ToString();
    }
    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
