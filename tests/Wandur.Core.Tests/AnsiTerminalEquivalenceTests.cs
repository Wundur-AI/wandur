using System.Text;
using Wandur.Core.Terminal;

namespace Wandur.Core.Tests;

/// <summary>The parser was reworked to allocate less; on any input it must produce exactly what it produced before.</summary>
public class AnsiTerminalEquivalenceTests
{
    private static readonly string[] Pieces =
    [
        "\u001b[0m", "\u001b[m", "\u001b[1m", "\u001b[22m", "\u001b[3m", "\u001b[23m", "\u001b[4m", "\u001b[24m", "\u001b[31m", "\u001b[1;30m",
        "\u001b[30;1m", "\u001b[97m", "\u001b[39m", "\u001b[49m", "\u001b[44m", "\u001b[104m", "\u001b[38;5;196m", "\u001b[38;5;3m", "\u001b[48;5;240m",
        "\u001b[38;2;10;20;300m", "\u001b[48;2;1;2m", "\u001b[38;5m", "\u001b[38m", "\u001b[;31m", "\u001b[31;m", "\u001b[ 32m", "\u001b[+33m",
        "\u001b[-1m", "\u001b[99999999999m", "\u001b[?25h", "\u001b[5:3m", "\u001b[K", "\u001b[0K", "\u001b[2K", "\u001b[1K", "\u001b[2J",
        "\u001b[3J", "\u001b[H", "\u001b]0;title\u0007", "\u001b]8;;http://x\u001b\\", "\u001bP payload \u001b\\", "\u001b(B", "\u001b7",
        "\r", "\n", "\r\n", "\b", "\t", "\u0007", "\u001b", "\u001b[", "\u001b[3", "hello", " world", "été", "x", "  ",
        "Exits: north east.", "<100hp 20m>", new string('w', 300), "\u001b[" + new string('1', 140) + "m"
    ];

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 5)]
    [InlineData(3, 2000)]
    [InlineData(4, 8)]
    [InlineData(5, 60)]
    public void ReworkedParserMatchesThePreviousOneOnRandomInput(int seed, int maxLines)
    {
        var random = new Random(seed);
        var current = new AnsiTerminal(maxLines);
        var reference = new ReferenceAnsiTerminal(maxLines);
        var completedCurrent = new List<string>();
        var completedReference = new List<string>();
        current.LineCompleted += line => completedCurrent.Add(Describe(line));
        reference.LineCompleted += line => completedReference.Add(Describe(line));
        for (var step = 0; step < 3000; step++)
        {
            var chunk = new StringBuilder();
            for (var n = random.Next(1, 12); n > 0; n--) chunk.Append(Pieces[random.Next(Pieces.Length)]);
            var text = chunk.ToString();
            // Split anywhere, including inside escape sequences, as socket reads do.
            var cut = random.Next(text.Length + 1);
            if (random.Next(10) == 0) { current.AppendLocalText(text); reference.AppendLocalText(text); }
            else
            {
                current.Append(text[..cut]); reference.Append(text[..cut]);
                current.Append(text[cut..]); reference.Append(text[cut..]);
            }
            if (random.Next(500) == 0) { current.Clear(); reference.Clear(); }
            if (step % 7 == 0 || step == 2999)
            {
                Assert.Equal(reference.Lines.Count, current.Lines.Count);
                for (var i = 0; i < reference.Lines.Count; i++) Assert.Equal(Describe(reference.Lines[i]), Describe(current.Lines[i]));
                Assert.Equal(reference.PlainText, current.PlainText);
                Assert.Equal(reference.CurrentLineTruncated, current.CurrentLineTruncated);
            }
        }
        Assert.Equal(completedReference, completedCurrent);
    }

    [Fact]
    public void LongTranscriptsEvictTheSameLinesAtTheCharacterLimit()
    {
        var current = new AnsiTerminal();
        var reference = new ReferenceAnsiTerminal();
        var line = "\u001b[32m" + new string('a', 150) + "\u001b[0m " + new string('b', 140) + "\r\n";
        for (var i = 0; i < 1500; i++) { current.Append(line + i); reference.Append(line + i); }
        Assert.Equal(reference.Lines.Count, current.Lines.Count);
        Assert.Equal(reference.PlainText, current.PlainText);
    }

    private static string Describe(TerminalLine line) =>
        string.Join("|", line.Runs.Select(run => $"{run.Text}#{run.Style}"));
}
