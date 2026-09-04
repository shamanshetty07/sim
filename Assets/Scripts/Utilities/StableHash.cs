using System.Text;

namespace Sim.Utilities
{
    /// <summary>
    /// Deterministic string hashing for anything that needs "same input -> same seed" behaviour
    /// across processes, platforms, and .NET versions — unlike <c>string.GetHashCode()</c>,
    /// which Microsoft explicitly does not guarantee to be stable across runs (it's randomized
    /// per-process by default in modern .NET for security reasons). Used wherever a mock/dev
    /// implementation needs to derive a reproducible seed from a prompt when the caller didn't
    /// supply one explicitly.
    /// </summary>
    public static class StableHash
    {
        private const uint FnvOffsetBasis = 2166136261;
        private const uint FnvPrime = 16777619;

        /// <summary>FNV-1a over the UTF-8 bytes of <paramref name="text"/>.</summary>
        public static int Fnv1a(string text)
        {
            uint hash = FnvOffsetBasis;
            foreach (byte b in Encoding.UTF8.GetBytes(text ?? string.Empty))
            {
                hash ^= b;
                hash *= FnvPrime;
            }

            return unchecked((int)hash);
        }
    }
}
