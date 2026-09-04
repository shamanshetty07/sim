using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Sim.AI
{
    /// <summary>
    /// Real credentials source: OS environment variables first, then a local, gitignored
    /// ".env.local" file at the repository root (sibling to Assets/, never under it — so it
    /// is never a Unity asset). The file is the primary mechanism for Editor use: a Unity
    /// Editor launched from Finder/Dock on macOS does not reliably inherit shell environment
    /// variables set in e.g. ~/.zshrc (a well-known platform quirk), so relying solely on
    /// process environment variables would silently fail to configure Reactor for most
    /// Editor users.
    ///
    /// This class never logs, exposes via any public property, or otherwise surfaces the raw
    /// credential value anywhere beyond the out parameter of these two methods. Callers must
    /// not log it either — see docs/OPENWORLD_REACTOR_INTEGRATION.md "Security requirements".
    ///
    /// Only meaningful in the Unity Editor / a local development machine. Application.dataPath
    /// in a built player points inside the build output, not the source repository, so the
    /// ".env.local" fallback does not apply to (and must never be relied on for) a shipped
    /// build — matching OpenWorld Reactor's own documented guidance to never ship the API key
    /// to a client. A shipped build needs a server-mediated credential flow, not this class.
    /// </summary>
    public sealed class EnvironmentReactorCredentialsProvider : IReactorCredentialsProvider
    {
        private const string LocalEnvFileName = ".env.local";
        private const string ApiKeyVariable = "OPENWORLD_REACTOR_API_KEY";
        private const string ModelVariable = "OPENWORLD_REACTOR_MODEL";

        private readonly Lazy<Dictionary<string, string>> _localFileValues;

        public EnvironmentReactorCredentialsProvider()
        {
            _localFileValues = new Lazy<Dictionary<string, string>>(ReadLocalEnvFile);
        }

        public bool TryGetApiKey(out string apiKey) => TryGetVariable(ApiKeyVariable, out apiKey);

        public bool TryGetModel(out string model) => TryGetVariable(ModelVariable, out model);

        private bool TryGetVariable(string name, out string value)
        {
            value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value)) return true;

            Dictionary<string, string> fileValues = _localFileValues.Value;
            if (fileValues != null && fileValues.TryGetValue(name, out value) && !string.IsNullOrEmpty(value))
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
                // "not found", not crash whatever called TryGetApiKey/TryGetModel.
                return null;
            }
        }
    }
}
