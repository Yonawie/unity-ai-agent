using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityAgent.Editor.Util;

namespace UnityAgent.Editor.Tools
{
    public static class ToolArgs
    {
        public static string Str(Dictionary<string, object> args, string key, string fallback = null)
            => AgentJson.GetString(args, key, fallback);

        public static bool Bool(Dictionary<string, object> args, string key, bool fallback = false)
            => AgentJson.GetBool(args, key, fallback);

        public static int Int(Dictionary<string, object> args, string key, int fallback = 0)
            => AgentJson.GetInt(args, key, fallback);

        public static float Float(Dictionary<string, object> args, string key, float fallback = 0f)
            => AgentJson.GetFloat(args, key, fallback);

        public static Vector3 Vec3(Dictionary<string, object> args, string key, Vector3 fallback)
        {
            if (args == null || !args.TryGetValue(key, out var value) || value == null)
                return fallback;

            if (value is List<object> list && list.Count >= 3)
            {
                return new Vector3(
                    ToFloat(list[0]),
                    ToFloat(list[1]),
                    ToFloat(list[2]));
            }

            if (value is Dictionary<string, object> obj)
            {
                return new Vector3(
                    AgentJson.GetFloat(obj, "x", fallback.x),
                    AgentJson.GetFloat(obj, "y", fallback.y),
                    AgentJson.GetFloat(obj, "z", fallback.z));
            }

            return fallback;
        }

        public static bool TryResolveGameObject(Dictionary<string, object> args, out GameObject go, out string error)
        {
            go = null;
            error = null;
            var id = Str(args, "id") ?? Str(args, "objectId");
            var name = Str(args, "name") ?? Str(args, "path");

            if (!string.IsNullOrEmpty(id) && ObjectIdUtil.TryResolve(id, out go))
                return true;

            if (!string.IsNullOrEmpty(name))
            {
                go = ObjectIdUtil.FindByPathOrName(name);
                if (go != null) return true;
            }

            error = $"GameObject not found (id='{id}', name='{name}'). Prefer stable id from previous tool results.";
            return false;
        }

        static float ToFloat(object value)
        {
            try { return Convert.ToSingle(value, CultureInfo.InvariantCulture); }
            catch { return 0f; }
        }
    }
}
