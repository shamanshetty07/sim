namespace Sim.WorldGeneration.Models
{
    /// <summary>
    /// The world-generation backend's own result envelope — OpenWorld Reactor's native
    /// output (or Mock standing in for it), *before* translation into Unity's normalized
    /// WorldSpecification. Deliberately not assumed to already look like WorldSpecification:
    /// nothing about Reactor's real output shape was discoverable in this environment (see
    /// docs/WORLD_SPECIFICATION.md), so this type is built to hold whichever of two very
    /// different possibilities turns out to be true — a structured description of a world, or
    /// a reference to an actual scene/asset representation the backend generated directly —
    /// without committing to either ahead of time. See <see cref="PayloadKind"/>.
    ///
    /// ReactorWorldAdapter is the only thing that reads the payload fields; everything else in
    /// the pipeline should go through the WorldSpecification the adapter produces.
    /// </summary>
    public sealed class ReactorWorldResult
    {
        public string WorldName { get; set; }
        public string Description { get; set; }

        /// <summary>The seed actually used for this generation — echoed back even if the request left it unspecified.</summary>
        public int Seed { get; set; }

        public WorldGenerationMetadata Metadata { get; set; }

        /// <summary>Which of the payload fields below is meaningful for this result.</summary>
        public ReactorWorldPayloadKind PayloadKind { get; set; } = ReactorWorldPayloadKind.Unknown;

        /// <summary>
        /// Populated when PayloadKind == StructuredData: the backend's own structured world
        /// description, in whatever shape it actually emits — NOT assumed to already match
        /// WorldSpecification's shape. Raw text (e.g. JSON) rather than a parsed object,
        /// because that shape is unknown; ReactorWorldAdapter is responsible for parsing it
        /// once a real shape exists to parse against (not yet implemented — see
        /// docs/WORLD_SPECIFICATION.md).
        /// </summary>
        public string StructuredPayloadJson { get; set; }

        /// <summary>
        /// Populated when PayloadKind == NativeSceneReference: a handle/URI/identifier the
        /// backend gives us to pull an actual scene/mesh/asset representation through a not-
        /// yet-known Unity integration mechanism. Carried through rather than discarded, so a
        /// future Unity-side loader has something to work from even before that mechanism is
        /// designed.
        /// </summary>
        public string NativeAssetReference { get; set; }

        /// <summary>
        /// Whether the backend guarantees the same request + seed reproduces the same result.
        /// Unknown/false by default until this is actually confirmed against a real backend —
        /// deterministic regeneration (a hard product requirement) must not be assumed true
        /// just because a seed field exists.
        /// </summary>
        public bool IsDeterministic { get; set; }
    }
}
