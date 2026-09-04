using System.Collections.Generic;

namespace Sim.WorldGeneration.Models
{
    /// <summary>
    /// Unity's own normalized, bounded description of what to build — the *only* input type
    /// WorldGenerator (still not implemented — see docs/IMPLEMENTATION_PLAN.md) will accept,
    /// always after validation.
    ///
    /// As of Phase 7, the authoritative producer of this type is the AI World Designer
    /// (<c>IWorldDesigner</c> — a general-purpose LLM interpreting the prompt directly into
    /// this shape), not OpenWorld Reactor: Reactor's real API was found (Phase 6.5,
    /// docs/REACTOR_TO_UNITY_ARCHITECTURE.md) to expose only live video, with no structured
    /// world data of any kind to adapt. <c>ReactorWorldAdapter</c>/<c>ReactorWorldResult</c>
    /// remain in the codebase (Reactor's future role is an optional, non-authoritative visual
    /// layer — see docs/AI_WORLD_DESIGNER.md), but they are no longer this type's primary
    /// source. If either producer turns out to generate richer content than this can express,
    /// the answer is to extend this type — not to force everything through a smaller, lossy
    /// model just because it existed first.
    ///
    /// <see cref="OriginalPrompt"/> is carried on every instance specifically so the prompt
    /// that produced a world is never lost between generation and save/load — it is not
    /// reduced to a biome string or discarded once terrain/environment fields are populated.
    /// </summary>
    public sealed class WorldSpecification
    {
        /// <summary>The user's complete original prompt. Never derived-away — see class remarks.</summary>
        public string OriginalPrompt { get; set; } = string.Empty;

        public string WorldName { get; set; } = "Generated World";
        public string Description { get; set; } = string.Empty;

        public int Seed { get; set; }
        public WorldScale Scale { get; set; } = WorldScale.Medium;

        /// <summary>What the prompt implies about how the world should fly — see class remarks on FlightCharacteristics for why this is separate from Terrain.</summary>
        public FlightCharacteristics Flight { get; set; } = new FlightCharacteristics();

        public TerrainSpecification Terrain { get; set; } = new TerrainSpecification();

        /// <summary>Open-ended list of environment object categories to place — see ObjectSpecification remarks.</summary>
        public List<ObjectSpecification> EnvironmentObjects { get; set; } = new List<ObjectSpecification>();

        public List<ObstacleSpecification> Obstacles { get; set; } = new List<ObstacleSpecification>();

        /// <summary>Race/gameplay-course intent (style, difficulty, gate count, section narrative) — see CourseSpecification remarks.</summary>
        public CourseSpecification Course { get; set; } = new CourseSpecification();

        public WeatherSpecification Weather { get; set; } = new WeatherSpecification();
        public LightingSpecification Lighting { get; set; } = new LightingSpecification();
        public SpawnSpecification Spawn { get; set; } = new SpawnSpecification();

        /// <summary>Provenance — which backend, which request, when. Carried through to Persistence (Phase 12/13).</summary>
        public WorldGenerationMetadata Metadata { get; set; }
    }
}
