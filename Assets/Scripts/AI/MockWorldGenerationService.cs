using System;
using System.Threading;
using System.Threading.Tasks;
using Sim.Utilities;
using Sim.WorldGeneration.Models;

namespace Sim.AI
{
    /// <summary>
    /// Development/testing stand-in for a real IWorldGenerationService. Exists so the rest of
    /// the application (adapter, validator, controller, future UI) can be built and tested
    /// without OpenWorld Reactor access, and so this project's async/cancellation/error-
    /// handling plumbing can be exercised deterministically in EditMode tests.
    ///
    /// IMPORTANT — this is explicitly NOT a fake AI: it does not parse or interpret the
    /// prompt to decide what to generate. A mock that pattern-matched "mountain" -> forest
    /// biome would be exactly the "hardcoded biome parser pretending to be AI" architecture
    /// this project was explicitly told to avoid. It always returns the same fixed example
    /// world; the prompt is only echoed into the description field so tests can confirm it
    /// survived the pipeline intact.
    ///
    /// What it does simulate, for real dev/testing value:
    ///  - Determinism: the same seed (explicit, or derived stably from the prompt when no
    ///    seed is given) always produces the same result — real world-generation backends are
    ///    expected to behave this way (see ReactorWorldResult.IsDeterministic), and having the
    ///    mock actually do it lets regeneration/seed-reproducibility logic be tested now.
    ///  - Simulated latency: SimulatedDelayMilliseconds (0 by default, so tests stay fast) can
    ///    be set to exercise async/cancellation code paths realistically.
    ///  - Cancellation: honors the CancellationToken during the simulated delay.
    /// </summary>
    public sealed class MockWorldGenerationService : IWorldGenerationService
    {
        /// <summary>0 by default (instant) — set higher only for manual/dev testing of loading states.</summary>
        public int SimulatedDelayMilliseconds { get; set; } = 0;

        public async Task<WorldGenerationOutcome> GenerateWorldAsync(WorldGenerationRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
                return WorldGenerationOutcome.Failed(WorldGenerationFailureReason.InvalidResponse, "Request was null.");

            if (SimulatedDelayMilliseconds > 0)
                await Task.Delay(SimulatedDelayMilliseconds, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            int seed = request.Seed ?? StableHash.Fnv1a(request.Prompt);

            var result = new ReactorWorldResult
            {
                WorldName = "Mock Example World",
                Description = $"Placeholder result — does not reflect the prompt. Prompt received: \"{request.Prompt}\"",
                Seed = seed,
                PayloadKind = ReactorWorldPayloadKind.Unknown,
                IsDeterministic = true, // true here specifically because seed resolution above is itself stable/reproducible
                Metadata = new WorldGenerationMetadata
                {
                    ProviderName = "Mock",
                    ProviderVersion = "phase6-placeholder",
                    RequestId = request.RequestId,
                    GeneratedAtUtc = DateTime.UtcNow,
                    GenerationDuration = TimeSpan.FromMilliseconds(SimulatedDelayMilliseconds)
                }
            };

            return WorldGenerationOutcome.Succeeded(result);
        }
    }
}
