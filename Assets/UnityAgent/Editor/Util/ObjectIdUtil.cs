using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityAgent.Editor.Util
{
    public static class ObjectIdUtil
    {
        public static string GetId(UnityEngine.Object obj)
        {
            if (obj == null) return null;
            try
            {
                return GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();
            }
            catch
            {
                return $"instance:{obj.GetInstanceID()}";
            }
        }

        public static bool TryResolve(string id, out GameObject go)
        {
            go = null;
            if (string.IsNullOrWhiteSpace(id)) return false;

            if (GlobalObjectId.TryParse(id, out var gid))
            {
                var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid);
                go = obj as GameObject;
                if (go == null && obj is Component c)
                    go = c.gameObject;
                return go != null;
            }

            if (id.StartsWith("instance:", StringComparison.Ordinal) &&
                int.TryParse(id.Substring("instance:".Length), out var instanceId))
            {
                var obj = EditorUtility.InstanceIDToObject(instanceId);
                go = obj as GameObject;
                if (go == null && obj is Component c)
                    go = c.gameObject;
                return go != null;
            }

            // Fallback by unique path / name within active scene (last resort).
            go = FindByPathOrName(id);
            return go != null;
        }

        public static GameObject FindByPathOrName(string nameOrPath)
        {
            if (string.IsNullOrWhiteSpace(nameOrPath)) return null;

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) return null;

            var roots = scene.GetRootGameObjects();
            foreach (var root in roots)
            {
                var match = FindRecursive(root.transform, nameOrPath, root.name);
                if (match != null) return match;
            }

            // Unique name search
            GameObject unique = null;
            var count = 0;
            foreach (var root in roots)
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != nameOrPath) continue;
                    count++;
                    unique = t.gameObject;
                }
            }

            return count == 1 ? unique : null;
        }

        static GameObject FindRecursive(Transform t, string pathOrName, string currentPath)
        {
            if (currentPath == pathOrName || t.name == pathOrName)
                return t.gameObject;

            for (var i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                var found = FindRecursive(child, pathOrName, currentPath + "/" + child.name);
                if (found != null) return found;
            }

            return null;
        }

        public static string GetHierarchyPath(GameObject go)
        {
            if (go == null) return null;
            var parts = new List<string>();
            var t = go.transform;
            while (t != null)
            {
                parts.Insert(0, t.name);
                t = t.parent;
            }
            return string.Join("/", parts);
        }

        public static Dictionary<string, object> Describe(GameObject go)
        {
            if (go == null) return null;
            return new Dictionary<string, object>
            {
                ["id"] = GetId(go),
                ["name"] = go.name,
                ["path"] = GetHierarchyPath(go),
                ["active"] = go.activeSelf,
                ["tag"] = go.tag,
                ["layer"] = go.layer,
                ["position"] = new List<object> { go.transform.position.x, go.transform.position.y, go.transform.position.z },
                ["rotation"] = new List<object> { go.transform.eulerAngles.x, go.transform.eulerAngles.y, go.transform.eulerAngles.z },
                ["scale"] = new List<object> { go.transform.localScale.x, go.transform.localScale.y, go.transform.localScale.z }
            };
        }
    }
}
