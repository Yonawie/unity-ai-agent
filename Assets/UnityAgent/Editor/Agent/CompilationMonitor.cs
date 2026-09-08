using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace UnityAgent.Editor.Agent
{
    /// <summary>
    /// Tracks Unity script compilation and domain reload boundaries.
    /// </summary>
    public static class CompilationMonitor
    {
        static TaskCompletionSource<bool> _compileTcs;
        static bool _hooksInstalled;

        public static bool IsCompiling => EditorApplication.isCompiling;

        public static void EnsureHooks()
        {
            if (_hooksInstalled) return;
            _hooksInstalled = true;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterReload;
            EditorApplication.update += OnUpdate;
        }

        static void OnUpdate()
        {
            if (_compileTcs != null && !EditorApplication.isCompiling)
            {
                _compileTcs.TrySetResult(true);
                _compileTcs = null;
            }
        }

        static void OnBeforeReload()
        {
            // Persistence layer handles session save.
        }

        static void OnAfterReload()
        {
            if (_compileTcs != null && !EditorApplication.isCompiling)
            {
                _compileTcs.TrySetResult(true);
                _compileTcs = null;
            }
        }

        public static async Task WaitForCompilationAsync(CancellationToken token, int timeoutMs = 180000)
        {
            EnsureHooks();

            // Give Unity a moment to start compiling after AssetDatabase.Refresh.
            var started = DateTime.UtcNow;
            while (!EditorApplication.isCompiling && (DateTime.UtcNow - started).TotalMilliseconds < 1500)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(50, token);
            }

            if (!EditorApplication.isCompiling)
                return;

            _compileTcs = new TaskCompletionSource<bool>();
            var delayTask = Task.Delay(timeoutMs, token);
            var completed = await Task.WhenAny(_compileTcs.Task, delayTask);
            if (completed == delayTask)
                throw new TimeoutException("Timed out waiting for Unity compilation.");

            // Extra settle time for console to populate.
            await Task.Delay(200, token);
        }
    }
}
