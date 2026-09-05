using System;
using Sim.WorldGeneration.Models;

namespace Sim.WorldGeneration.Persistence
{
    /// <summary>
    /// The complete, authoritative input needed to deterministically recreate one generated
    /// world — never the generated world itself. Composition over duplication: wraps the
    /// existing <see cref="WorldSpecification"/>/<see cref="WorldGenerationMetadata"/> types
    /// rather than re-declaring their fields here.
    ///
    /// <see cref="Prompt"/> and <see cref="Seed"/> are also surfaced as their own top-level
    /// properties even though <see cref="WorldSpecification.OriginalPrompt"/>/
    /// <see cref="WorldSpecification.Seed"/> already carry the same values — they are this
    /// save's own primary identity, not a second independent source of truth: the only
    /// supported construction path (<see cref="FromSpecification"/>) always copies them
    /// straight from <see cref="Specification"/>, and <see cref="WorldSaveValidator"/> rejects
    /// a save file where they've drifted apart (e.g. hand-edited on disk).
    ///
    /// Deliberately does NOT contain: any GameObject/Component/Terrain/Transform/Rigidbody
    /// reference, generated mesh/collider data, a Unity object instance ID, an event
    /// subscription/delegate, an API key, or any other credential/authentication state. Every
    /// property here is a plain, JSON-serializable value — see WorldSaveJsonSerializer for the
    /// one place this ever becomes bytes, and WorldSaveService for the one place those bytes
    /// ever touch disk.
    /// </summary>
    public sealed class WorldSaveData
    {
        /// <summary>The current save-file format version this class writes. Bump when WorldSaveData's own shape changes incompatibly — WorldSaveValidator rejects anything else rather than silently reinterpreting it.</summary>
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;

        /// <summary>The user's complete, unmodified original prompt — never rewritten, normalized, or parsed. Mirrors Specification.OriginalPrompt exactly.</summary>
        public string Prompt { get; set; }

        /// <summary>Mirrors Specification.Seed exactly — authoritative for deterministic reconstruction. Loading never generates a new seed.</summary>
        public int Seed { get; set; }

        /// <summary>The validated (at save time) world definition — the one thing WorldGenerator actually needs to recreate the world.</summary>
        public WorldSpecification Specification { get; set; }

        /// <summary>Provenance of the generation this save captures — the same instance as Specification.Metadata, surfaced here too for convenient top-level access. May be null if the specification never had one.</summary>
        public WorldGenerationMetadata Metadata { get; set; }

        /// <summary>When this WorldSaveData was actually written to disk — set by WorldSaveService.Save, not by the caller.</summary>
        public DateTime SavedAtUtc { get; set; }

        /// <summary>
        /// Builds a WorldSaveData from an already-validated WorldSpecification — the only
        /// supported construction path, so Prompt/Seed/Metadata can never drift from
        /// Specification's own values at the moment of saving.
        /// </summary>
        public static WorldSaveData FromSpecification(WorldSpecification specification)
        {
            if (specification == null)
                throw new ArgumentNullException(nameof(specification));

            return new WorldSaveData
            {
                Version = CurrentVersion,
                Prompt = specification.OriginalPrompt,
                Seed = specification.Seed,
                Specification = specification,
                Metadata = specification.Metadata,
                SavedAtUtc = DateTime.UtcNow
            };
        }
    }
}
