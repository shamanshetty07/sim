using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Sim.AI.WorldDesign
{
    /// <summary>
    /// OS environment variable first, then a local, gitignored ".env.local" file at the
    /// repository root — the same dual-lookup pattern
    /// Sim.AI.EnvironmentReactorCredentialsProvider already established for OpenWorld Reactor
    /// (kept untouched — Reactor integration is out of scope for this phase), generalized here
    /// by variable name rather than hardcoded to one provider's specific variable names, so any
    /// ILLMClient in this namespace can reuse it instead of each re-implementing the same
    /// env-var-then-file lookup. AI_WORLD_DESIGNER.md (Phase 7) flagged adopting this pattern as
    /// "a reasonable next step once any of these gets real credentials" — this is that step.
    ///
    /// Same rationale as the Reactor version for why the file fallback matters: a Unity Editor
    /// launched from Finder/Dock on macOS does not reliably inherit shell environment variables,
    /// so relying solely on process environment variables would silently fail to configure a
    /// provider for most Editor users. Only meaningful in the Editor/a local dev machine —
    /// Application.dataPath in a built Player points inside the build output, not the source
    /// repository, so the ".env.local" fallback does not apply there; a shipped build needs a
    /// server-mediated credential flow, not this class (same caveat as Reactor's).
    ///
    /// Never logs, exposes via a public property, or otherwise surfaces a raw value anywhere
    /// beyond the out parameter of TryGetVariable. Callers must not log it either.
    /// </summary>
    public sealed class EnvironmentLlmCredentialsProvider
    {
        private const string LocalEnvFileName = ".env.local";

        private readonly Lazy<Dictionary<string, string>> _localFileValues;

        public EnvironmentLlmCredentialsProvider()
        {
            _localFileValues = new Lazy<Dictionary<string, string>>(ReadLocalEnvFile);
        }

        public bool TryGetVariable(string variableName, out string value)
        {
            value = Environment.GetEnvironmentVariable(variableName);
            if (!string.IsNullOrEmpty(value)) return true;

            Dictionary<string, string> fileValues = _localFileValues.Value;
            if (fileValues != null && fileValues.TryGetValue(variableName, out value) && !string.IsNullOrEmpty(value))
                return true;

            value = null;
            return false;
        }

        private static Dictionary<string, string> ReadLocalEnvFile()
        {
            try
            {
                string path = Path.Combine(Application.dataPath, "..", LocalEnvFileName);
                if (!File.Exists(path)) return null;

                var result = new Dictionary<string, string>();
                foreach (string rawLine in File.ReadAllLines(path))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;

                    int separatorIndex = line.IndexOf('=');
                    if (separatorIndex <= 0) continue;

                    string key = line.Substring(0, separatorIndex).Trim();
                    string val = line.Substring(separatorIndex + 1).Trim();
                    result[key] = val;
                }

                return result;
            }
            catch (Exception)
            {
                // A config-read failure (permissions, unexpected format) should present as
                // "not found", not crash whatever called TryGetVariable.
                return null;
            }
        }
    }
}
