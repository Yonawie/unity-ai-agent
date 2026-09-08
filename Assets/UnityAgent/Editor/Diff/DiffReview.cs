using System;

namespace UnityAgent.Editor.Diff
{
    /// <summary>Holds the last script diff for the Agent window panel.</summary>
    public static class DiffReview
    {
        public static string Path;
        public static string Brief;
        public static string UnifiedDiff;
        public static DateTime UpdatedUtc;

        public static void Set(string path, string brief, string diff)
        {
            Path = path;
            Brief = brief;
            UnifiedDiff = diff;
            UpdatedUtc = DateTime.UtcNow;
        }

        public static void Clear()
        {
            Path = null;
            Brief = null;
            UnifiedDiff = null;
        }
    }
}
