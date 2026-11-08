namespace ClickBoard.App.ViewModels;

internal sealed record ComparisonLine(string Left, string Right, bool Removed, bool Added);

internal sealed record LineComparison(IReadOnlyList<ComparisonLine> Lines, int Added, int Removed, bool Simplified)
{
    public static LineComparison Create(string first, string second)
    {
        var left = first.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var right = second.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var rows = new List<ComparisonLine>();
        int a = 0, b = 0, added = 0, removed = 0;

        string Label(int number, string text) => $"{number + 1,5}  {text}";
        void Same() { rows.Add(new(Label(a, left[a]), Label(b, right[b]), false, false)); a++; b++; }
        void Remove() { rows.Add(new(Label(a, left[a]), "", true, false)); a++; removed++; }
        void Add() { rows.Add(new("", Label(b, right[b]), false, true)); b++; added++; }

        var simplified = (long)left.Length * right.Length > 4_000_000;
        if (!simplified)
        {
            var lengths = new int[left.Length + 1, right.Length + 1];
            for (var i = left.Length - 1; i >= 0; i--)
            {
                for (var j = right.Length - 1; j >= 0; j--)
                {
                    lengths[i, j] = left[i] == right[j]
                        ? lengths[i + 1, j + 1] + 1
                        : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
                }
            }
            while (a < left.Length && b < right.Length)
            {
                if (left[a] == right[b]) Same();
                else if (lengths[a + 1, b] >= lengths[a, b + 1]) Remove();
                else Add();
            }
        }
        else
        {
            // Bound memory for large clips; preserve equal prefix and suffix exactly.
            while (a < left.Length && b < right.Length && left[a] == right[b])
                Same();
            var suffix = 0;
            while (left.Length - suffix > a && right.Length - suffix > b && left[^(suffix + 1)] == right[^(suffix + 1)])
                suffix++;
            while (a < left.Length - suffix)
                Remove();
            while (b < right.Length - suffix)
                Add();
            while (a < left.Length && b < right.Length)
                Same();
        }
        while (a < left.Length) Remove();
        while (b < right.Length) Add();
        return new(rows, added, removed, simplified);
    }
}
