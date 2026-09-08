using System;
using System.Collections.Generic;
using System.Text;

namespace UnityAgent.Editor.Diff
{
    public static class TextDiff
    {
        public static string Unified(string oldText, string newText, string path, int maxLines = 200)
        {
            oldText ??= string.Empty;
            newText ??= string.Empty;
            var a = SplitLines(oldText);
            var b = SplitLines(newText);
            var ops = MyersDiff(a, b);

            var sb = new StringBuilder();
            sb.AppendLine($"--- a/{path}");
            sb.AppendLine($"+++ b/{path}");
            sb.AppendLine($"@@ {Brief(oldText, newText)} @@");

            var emitted = 0;
            foreach (var op in ops)
            {
                if (emitted >= maxLines)
                {
                    sb.AppendLine("... diff truncated ...");
                    break;
                }

                switch (op.Kind)
                {
                    case DiffOpKind.Equal:
                        sb.AppendLine(" " + op.Line);
                        break;
                    case DiffOpKind.Delete:
                        sb.AppendLine("-" + op.Line);
                        break;
                    case DiffOpKind.Insert:
                        sb.AppendLine("+" + op.Line);
                        break;
                }
                emitted++;
            }

            return sb.ToString();
        }

        public static string Brief(string oldText, string newText)
        {
            var a = SplitLines(oldText ?? string.Empty);
            var b = SplitLines(newText ?? string.Empty);
            var ops = MyersDiff(a, b);
            var del = 0;
            var ins = 0;
            foreach (var op in ops)
            {
                if (op.Kind == DiffOpKind.Delete) del++;
                else if (op.Kind == DiffOpKind.Insert) ins++;
            }
            return $"{a.Length} → {b.Length} lines (-{del}/+{ins})";
        }

        enum DiffOpKind { Equal, Insert, Delete }

        readonly struct DiffOp
        {
            public DiffOpKind Kind;
            public string Line;
            public DiffOp(DiffOpKind kind, string line) { Kind = kind; Line = line; }
        }

        static List<DiffOp> MyersDiff(string[] a, string[] b)
        {
            // Patience-free LCS backtrack — good enough for script review.
            var n = a.Length;
            var m = b.Length;
            var dp = new int[n + 1, m + 1];
            for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                dp[i, j] = a[i] == b[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);

            var ops = new List<DiffOp>();
            var x = 0;
            var y = 0;
            while (x < n && y < m)
            {
                if (a[x] == b[y])
                {
                    ops.Add(new DiffOp(DiffOpKind.Equal, a[x]));
                    x++; y++;
                }
                else if (dp[x + 1, y] >= dp[x, y + 1])
                {
                    ops.Add(new DiffOp(DiffOpKind.Delete, a[x]));
                    x++;
                }
                else
                {
                    ops.Add(new DiffOp(DiffOpKind.Insert, b[y]));
                    y++;
                }
            }
            while (x < n) ops.Add(new DiffOp(DiffOpKind.Delete, a[x++]));
            while (y < m) ops.Add(new DiffOp(DiffOpKind.Insert, b[y++]));
            return ops;
        }

        static string[] SplitLines(string text) =>
            text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }
}
