using RapidOCRLib.Models;

namespace SnipTranslate.OcrWorker;

internal static class TextLayoutReflow
{
    internal static string Arrange(IReadOnlyCollection<TextBlock>? blocks, string fallback)
    {
        if (blocks is null || blocks.Count == 0)
        {
            return fallback.Trim();
        }

        var items = blocks
            .Where(block => !string.IsNullOrWhiteSpace(block.Text) && block.BoxPoints.Count > 0)
            .Select(block =>
            {
                var left = block.BoxPoints.Min(point => point.X);
                var right = block.BoxPoints.Max(point => point.X);
                var top = block.BoxPoints.Min(point => point.Y);
                var bottom = block.BoxPoints.Max(point => point.Y);
                return new LayoutItem(block.Text.Trim(), left, top, right, bottom);
            })
            .OrderBy(item => item.CenterY)
            .ThenBy(item => item.Left)
            .ToList();

        if (items.Count == 0)
        {
            return fallback.Trim();
        }

        var lines = new List<LayoutLine>();
        foreach (var item in items)
        {
            var line = lines
                .Where(candidate => VerticalOverlap(candidate, item) >= 0.45)
                .OrderBy(candidate => Math.Abs(candidate.CenterY - item.CenterY))
                .FirstOrDefault();

            if (line is null)
            {
                lines.Add(new LayoutLine(item));
            }
            else
            {
                line.Add(item);
            }
        }

        lines = lines.OrderBy(line => line.Top).ThenBy(line => line.Left).ToList();
        var medianHeight = lines.Select(line => line.Height).OrderBy(value => value).ElementAt(lines.Count / 2);
        var paragraphs = new List<string>();
        var current = new List<string>();
        LayoutLine? previous = null;

        foreach (var line in lines)
        {
            var startsNewParagraph = previous is not null &&
                                     (line.Top - previous.Bottom > medianHeight * 1.15 ||
                                      LooksLikeColumnJump(previous, line, medianHeight));
            if (startsNewParagraph && current.Count > 0)
            {
                paragraphs.Add(string.Join(Environment.NewLine, current));
                current.Clear();
            }

            current.Add(JoinLine(line.Items));
            previous = line;
        }

        if (current.Count > 0)
        {
            paragraphs.Add(string.Join(Environment.NewLine, current));
        }

        return string.Join(Environment.NewLine + Environment.NewLine, paragraphs).Trim();
    }

    private static string JoinLine(IReadOnlyList<LayoutItem> items)
    {
        var ordered = items.OrderBy(item => item.Left).ToArray();
        if (ordered.Length == 1)
        {
            return ordered[0].Text;
        }

        var parts = new List<string> { ordered[0].Text };
        for (var index = 1; index < ordered.Length; index++)
        {
            var previous = ordered[index - 1];
            var current = ordered[index];
            var gap = current.Left - previous.Right;
            if (NeedsSpace(previous.Text, current.Text, gap, Math.Max(previous.Height, current.Height)))
            {
                parts.Add(" ");
            }

            parts.Add(current.Text);
        }

        return string.Concat(parts);
    }

    private static bool NeedsSpace(string left, string right, double gap, double height)
    {
        if (gap <= height * 0.08 || left.Length == 0 || right.Length == 0)
        {
            return false;
        }

        var leftCharacter = left[^1];
        var rightCharacter = right[0];
        return IsLatinOrDigit(leftCharacter) && IsLatinOrDigit(rightCharacter);
    }

    private static bool IsLatinOrDigit(char character) =>
        character <= 0x024F && char.IsLetterOrDigit(character);

    private static double VerticalOverlap(LayoutLine line, LayoutItem item)
    {
        var overlap = Math.Max(0, Math.Min(line.Bottom, item.Bottom) - Math.Max(line.Top, item.Top));
        return overlap / Math.Max(1, Math.Min(line.Height, item.Height));
    }

    private static bool LooksLikeColumnJump(LayoutLine previous, LayoutLine current, double medianHeight) =>
        current.Top < previous.Bottom && Math.Abs(current.Left - previous.Left) > medianHeight * 8;

    private sealed record LayoutItem(string Text, double Left, double Top, double Right, double Bottom)
    {
        internal double CenterY => (Top + Bottom) / 2;
        internal double Height => Math.Max(1, Bottom - Top);
    }

    private sealed class LayoutLine
    {
        internal LayoutLine(LayoutItem item)
        {
            Items.Add(item);
            UpdateBounds();
        }

        internal List<LayoutItem> Items { get; } = [];
        internal double Left { get; private set; }
        internal double Top { get; private set; }
        internal double Bottom { get; private set; }
        internal double Height => Math.Max(1, Bottom - Top);
        internal double CenterY => (Top + Bottom) / 2;

        internal void Add(LayoutItem item)
        {
            Items.Add(item);
            UpdateBounds();
        }

        private void UpdateBounds()
        {
            Left = Items.Min(item => item.Left);
            Top = Items.Min(item => item.Top);
            Bottom = Items.Max(item => item.Bottom);
        }
    }
}

