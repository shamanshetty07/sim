using System;
using System.Threading;
using System.Threading.Tasks;
using Sim.WorldGeneration.Models;

namespace Sim.AI
{
    /// <summary>
    /// Minimal IWorldGenerationService implementation for exercising the pipeline before real
    /// OpenWorld Reactor access exists. Intentionally does NOT parse or interpret the prompt —
    /// it returns one fixed example result regardless of what was asked for. Building a mock
    /// that actually varies its output usefully (multiple example worlds, simple keyword
    /// selection for dev convenience) is Phase 6 work; this exists in Phase 5 only to prove
    /// IWorldGenerationService -> WorldGenerationOutcome -> ReactorWorldResult is a real,
    /// usable, compiling contract end to end, and to give ReactorWorldAdapter something to
    /// run against.
    ///
    /// This is a deliberate choice, not a shortcut: a mock that pretended to interpret the
    /// prompt (e.g. keyword-matching "mountain" to a hardcoded biome) is exactly the
    /// "hardcoded biome parser pretending to be AI" architecture this phase was explicitly
    /// told to move away from. Keeping the mock honestly non-interpretive means nothing about
    /// its behaviour can be mistaken for how the real backend will behave.
    /// </summary>
    public sealed class MockWorldGenerationService : IWorldGenerationService
    {
        public Task<WorldGenerationOutcome> GenerateWorldAsync(WorldGenerationRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
                return Task.FromResult(WorldGenerationOutcome.Failed(WorldGenerationFailureReason.InvalidResponse, "Request was null."));

            var result = new ReactorWorldResult
            {
                WorldName = "Mock Example World",
                Description = $"Placeholder result — does not reflect the prompt. Prompt received: \"{request.Prompt}\"",
                Seed = request.Seed ?? new System.Random().Next(),
                PayloadKind = ReactorWorldPayloadKind.Unknown,
                IsDeterministic = false, // honest: this mock's Seed fallback above is NOT deterministic when request.Seed is null
                Metadata = new WorldGenerationMetadata
                {
                    ProviderName = "Mock",
                    ProviderVersion = "phase5-placeholder",
                    RequestId = request.RequestId,
                    GeneratedAtUtc = DateTime.UtcNow,
                    GenerationDuration = TimeSpan.Zero
                }
            };

            return Task.FromResult(WorldGenerationOutcome.Succeeded(result));
        }
    }
}
