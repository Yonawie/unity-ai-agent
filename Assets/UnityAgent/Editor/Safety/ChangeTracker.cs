using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityAgent.Editor.Tools;
using UnityAgent.Editor.Util;
using UnityEditor;
using UnityEngine;

namespace UnityAgent.Editor.Safety
{
    [Serializable]
    public class ChangeRecord
    {
        public string Kind; // Created | Modified | Deleted
        public string Target;
        public string Detail;
        public string BackupPath;
        public int UndoGroup = -1;
    }

    public sealed class ChangeTracker
    {
        public List<ChangeRecord> Records { get; } = new List<ChangeRecord>();
        int _undoGroup = -1;

        public void BeginTask(string label)
        {
            Records.Clear();
            Undo.SetCurrentGroupName($"UnityAgent: {label}");
            _undoGroup = Undo.GetCurrentGroup();
        }

        public void RecordCreated(string target, string detail = null)
        {
            Records.Add(new ChangeRecord { Kind = "Created", Target = target, Detail = detail, UndoGroup = _undoGroup });
        }

        public void RecordModified(string target, string detail = null, string backupPath = null)
        {
            Records.Add(new ChangeRecord { Kind = "Modified", Target = target, Detail = detail, BackupPath = backupPath, UndoGroup = _undoGroup });
        }

        public void RecordDeleted(string target, string detail = null, string backupPath = null)
        {
            Records.Add(new ChangeRecord { Kind = "Deleted", Target = target, Detail = detail, BackupPath = backupPath, UndoGroup = _undoGroup });
        }

        public void RecordTool(string toolName, ToolResult result)
        {
            if (result?.Data is Dictionary<string, object> data)
            {
                if (data.TryGetValue("id", out var id) || data.TryGetValue("path", out id))
                {
                    var target = Convert.ToString(id);
                    if (toolName.StartsWith("create", StringComparison.OrdinalIgnoreCase))
                        RecordCreated(target, toolName);
                    else if (toolName.StartsWith("delete", StringComparison.OrdinalIgnoreCase))
                        RecordDeleted(target, toolName);
                    else if (toolName.StartsWith("patch", StringComparison.OrdinalIgnoreCase) ||
                             toolName.StartsWith("set_", StringComparison.OrdinalIgnoreCase) ||
                             toolName.StartsWith("add_", StringComparison.OrdinalIgnoreCase) ||
                             toolName.StartsWith("remove_", StringComparison.OrdinalIgnoreCase) ||
                             toolName.StartsWith("rename", StringComparison.OrdinalIgnoreCase))
                        RecordModified(target, toolName);
                }
            }
        }

        public string Summarize()
        {
            var sb = new StringBuilder();
            var created = Records.FindAll(r => r.Kind == "Created");
            var modified = Records.FindAll(r => r.Kind == "Modified");
            var deleted = Records.FindAll(r => r.Kind == "Deleted");

            if (created.Count > 0)
            {
                sb.AppendLine("Created:");
                foreach (var r in created) sb.AppendLine($"* {r.Target}");
            }
            if (modified.Count > 0)
            {
                sb.AppendLine("Modified:");
                foreach (var r in modified) sb.AppendLine($"~ {r.Target}");
            }
            if (deleted.Count > 0)
            {
                sb.AppendLine("Deleted:");
                foreach (var r in deleted) sb.AppendLine($"* {r.Target}");
            }

            return sb.Length == 0 ? "No tracked changes." : sb.ToString().TrimEnd();
        }

        public bool TryUndoTask(out string message)
        {
            try
            {
                // Restore file backups first.
                foreach (var record in Records)
                {
                    if (string.IsNullOrEmpty(record.BackupPath) || !File.Exists(record.BackupPath))
                        continue;

                    if (record.Kind == "Modified" && !string.IsNullOrEmpty(record.Target) && record.Target.StartsWith("Assets/"))
                    {
                        File.Copy(record.BackupPath, record.Target, true);
                    }
                    else if (record.Kind == "Created" && !string.IsNullOrEmpty(record.Target) && File.Exists(record.Target))
                    {
                        AssetDatabase.DeleteAsset(record.Target);
                    }
                    else if (record.Kind == "Deleted" && !string.IsNullOrEmpty(record.Target) && File.Exists(record.BackupPath))
                    {
                        var dir = Path.GetDirectoryName(record.Target);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);
                        File.Copy(record.BackupPath, record.Target, true);
                    }
                }

                if (_undoGroup >= 0)
                    Undo.RevertAllDownToGroup(_undoGroup);

                AssetDatabase.Refresh();
                message = "Task changes reverted where possible.";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Undo failed: {ex.Message}";
                return false;
            }
        }

        public static string BackupFile(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !File.Exists(assetPath))
                return null;

            var backupRoot = Path.Combine("Library", "UnityAgent", "Backups");
            Directory.CreateDirectory(backupRoot);
            var fileName = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "_" + Path.GetFileName(assetPath);
            var backupPath = Path.Combine(backupRoot, fileName);
            File.Copy(assetPath, backupPath, true);
            return backupPath;
        }
    }
}
