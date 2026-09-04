using System;
using Sim.WorldGeneration.Models;
using UnityEngine;

namespace Sim.WorldGeneration.Adapters
{
    /// <summary>
    /// Default IReactorWorldAdapter. As of Phase 5 this is intentionally minimal: it maps the
    /// fields of ReactorWorldResult that are safe to interpret regardless of backend (name,
    /// description, seed, metadata, and the original prompt from the request) straight onto a
    /// new WorldSpecification, and leaves every content field (Terrain/EnvironmentObjects/
    /// Obstacles/Weather/Lighting/Spawn/Flight) at its own sensible default.
    ///
    /// It deliberately does NOT parse <see cref="ReactorWorldResult.StructuredPayloadJson"/> or
    /// resolve <see cref="ReactorWorldResult.NativeAssetReference"/> — there is no real payload
    /// shape to parse against yet (see docs/WORLD_SPECIFICATION.md "Open questions"). Adding
    /// that parsing is Phase 6/7 work, once either a real OpenWorld Reactor payload or a
    /// deliberately-shaped Mock payload exists to write it against. When that parsing is
    /// added, it must fail closed on malformed input (reject, don't guess) — see
    /// docs/ARCHITECTURE.md §7's error-handling table.
    /// </summary>
    public sealed class ReactorWorldAdapter : IReactorWorldAdapter
    {
        public WorldSpecification Adapt(ReactorWorldResult result, WorldGenerationRequest originalRequest)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (originalRequest == null) throw new ArgumentNullException(nameof(originalRequest));

            var spec = new WorldSpecification
            {
                OriginalPrompt = originalRequest.Prompt,
                WorldName = string.IsNullOrWhiteSpace(result.WorldName) ? "Generated World" : result.WorldName,
                Description = result.Description ?? string.Empty,
                Seed = result.Seed,
                Metadata = result.Metadata,
                Scale = originalRequest.RequestedScale ?? WorldScale.Medium
            };

            switch (result.PayloadKind)
            {
                case ReactorWorldPayloadKind.StructuredData:
                    // TODO(Phase 6/7): parse result.StructuredPayloadJson once a real or
                    // deliberately-designed Mock payload shape exists to parse against. Until
                    // then, WorldSpecification's content fields stay at their defaults rather
                    // than guessing at a shape.
                    Debug.LogWarning(
                        "ReactorWorldAdapter: received a StructuredData payload but does not parse it yet " +
                        "(Phase 5 scope). WorldSpecification content fields are defaults, not derived from the payload.");
                    break;

                case ReactorWorldPayloadKind.NativeSceneReference:
                    // TODO(Phase 7+): once Unity's integration mechanism for a native asset
                    // reference is known, this is where it gets resolved or carried forward
                    // for WorldGenerator to consume directly.
                    Debug.LogWarning(
                        "ReactorWorldAdapter: received a NativeSceneReference payload but has no Unity integration " +
                        "mechanism for it yet (Phase 5 scope). WorldSpecification content fields are defaults.");
                    break;

                case ReactorWorldPayloadKind.Unknown:
                default:
                    // Nothing further to do — WorldSpecification's content-field defaults are
                    // exactly what's appropriate for "no payload to interpret".
                    break;
            }

            return spec;
        }
    }
}
