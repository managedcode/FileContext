using System.Text;

using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Util;

namespace ManagedCode.FileContext.Pdf;

/// <summary>
///     Streams a page's letters in PDF content order. PdfPig materializes its glyph list when it parses the page;
///     this reader avoids the additional unbounded text and normalization strings created by GetText.
/// </summary>
internal static class FileContextPdfPageReader
{
    private const string Space = " ";
    private const double NewlinePointSizeRatio = 0.9;
    private const int MaximumPendingNewlines = 2;

    public static FileContextPdfPageText Read(Page page, int maxRetainedChars)
    {
        var output = new NormalizedPrefix(maxRetainedChars);
        Letter? previous = null;
        Letter? lastNonWhitespace = null;

        foreach (var letter in page.Letters)
        {
            if (string.IsNullOrEmpty(letter.Value))
            {
                continue;
            }

            if (string.Equals(letter.Value, Space, StringComparison.Ordinal))
            {
                if (previous is null || !IsNewline(previous, letter))
                {
                    output.Append(Space);
                    previous = letter;
                }

                continue;
            }

            if (previous is not null)
            {
                if (lastNonWhitespace is not null && IsNewline(lastNonWhitespace, letter))
                {
                    output.Append("\n");
                }
                else if (!string.Equals(previous.Value, Space, StringComparison.Ordinal) && WhitespaceSizeStatistics.IsProbablyWhitespace(
                             letter.StartBaseLine.X - previous.EndBaseLine.X, previous))
                {
                    output.Append(Space);
                }
            }

            output.Append(letter.Value);
            previous = letter;
            if (!string.IsNullOrWhiteSpace(letter.Value))
            {
                lastNonWhitespace = letter;
            }
        }

        return new FileContextPdfPageText(output.Text, output.TextCut, output.HasTextLayer);
    }

    private static bool IsNewline(Letter previous, Letter current)
    {
        var pointSize = Math.Min(Math.Round(previous.PointSize), Math.Round(current.PointSize));
        return Math.Abs(previous.StartBaseLine.Y - current.StartBaseLine.Y) > pointSize * NewlinePointSizeRatio;
    }

    private sealed class NormalizedPrefix(int maxRetainedChars)
    {
        private readonly StringBuilder text = new(Math.Min(maxRetainedChars, 1024));
        private bool pendingSpace;
        private int pendingNewlines;
        private bool hasEmitted;
        private bool inWord;
        private int wordCount;
        private int letterCount;

        public string Text => text.ToString();
        public bool TextCut { get; private set; }
        public bool HasTextLayer => wordCount >= FileContextPdfTextExtractor.MinWordsForTextPage
                                    || letterCount >= FileContextPdfTextExtractor.MinLettersForTextPage;

        public void Append(string value)
        {
            foreach (var character in value)
            {
                if (character is '\r' or '\n')
                {
                    pendingSpace = false;
                    pendingNewlines = Math.Min(MaximumPendingNewlines, pendingNewlines + 1);
                    continue;
                }

                if (char.IsWhiteSpace(character))
                {
                    if (pendingNewlines == 0)
                    {
                        pendingSpace = true;
                    }

                    continue;
                }

                FlushWhitespace();
                Emit(character);
            }
        }

        private void FlushWhitespace()
        {
            if (hasEmitted)
            {
                for (var index = 0; index < pendingNewlines; index++)
                {
                    Emit('\n');
                }

                if (pendingNewlines == 0 && pendingSpace)
                {
                    Emit(' ');
                }
            }

            pendingNewlines = 0;
            pendingSpace = false;
        }

        private void Emit(char character)
        {
            var whitespace = char.IsWhiteSpace(character);
            if (whitespace)
            {
                inWord = false;
            }
            else
            {
                if (!inWord)
                {
                    wordCount = Math.Min(FileContextPdfTextExtractor.MinWordsForTextPage, wordCount + 1);
                    inWord = true;
                }

                if (char.IsLetterOrDigit(character))
                {
                    letterCount = Math.Min(FileContextPdfTextExtractor.MinLettersForTextPage, letterCount + 1);
                }
            }

            if (text.Length < maxRetainedChars)
            {
                text.Append(character);
            }
            else
            {
                TextCut = true;
            }

            hasEmitted = true;
        }
    }
}
