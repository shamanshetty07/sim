using System;
using Sim.WorldGeneration.Validation;

namespace Sim.WorldGeneration.Persistence
{
    /// <summary>
    /// Save-envelope validation only — WorldSpecificationValidator (Sim.WorldGeneration.
    /// Validation) remains the sole authority on Specification's own field-by-field validity;
    /// this class never duplicates any of its rules, it only calls into it and folds the result
    /// in. What this class alone checks is the small set of things specific to the save
    /// envelope itself: is the version supported, is the prompt present and reasonably sized, is
    /// the specification present at all, and does the envelope's own Prompt/Seed agree with
    /// Specification's (a hand-edited or corrupted save file could disagree; a save written by
    /// WorldSaveData.FromSpecification never can).
    ///
    /// A save file is untrusted input, exactly like LLM output — this is the boundary that
    /// guarantees loading a save can never bypass WorldSpecificationValidator, per this phase's
    /// explicit requirement.
    /// </summary>
    public static class WorldSaveValidator
    {
        /// <summary>Generous but finite — a save file is untrusted input, and a multi-megabyte "prompt" string is not a legitimate one, whatever produced the file.</summary>
        public const int MaxPromptLength = 8000;

        public static WorldLoadValidationResult Validate(WorldSaveData data, IWorldSpecificationValidator specificationValidator = null)
        {
            if (data == null)
                return WorldLoadValidationResult.Failed("Save data is missing.");

            if (data.Version != WorldSaveData.CurrentVersion)
                return WorldLoadValidationResult.Failed(
                    $"Unsupported save version {data.Version} (this build supports version {WorldSaveData.CurrentVersion}).");

            if (string.IsNullOrWhiteSpace(data.Prompt))
                return WorldLoadValidationResult.Failed("Save data has no prompt.");

            if (data.Prompt.Length > MaxPromptLength)
                return WorldLoadValidationResult.Failed($"Save data prompt exceeds the maximum allowed length ({MaxPromptLength} characters).");

            if (data.Specification == null)
                return WorldLoadValidationResult.Failed("Save data has no world specification.");

            if (!string.Equals(data.Prompt, data.Specification.OriginalPrompt, StringComparison.Ordinal))
                return WorldLoadValidationResult.Failed("Save data's prompt does not match its specification's prompt.");

            if (data.Seed != data.Specification.Seed)
                return WorldLoadValidationResult.Failed("Save data's seed does not match its specification's seed.");

            IWorldSpecificationValidator validator = specificationValidator ?? new WorldSpecificationValidator();
            ValidationResult result = validator.Validate(data.Specification);

            if (!result.IsValid || result.RepairedSpecification == null)
                return WorldLoadValidationResult.Failed("Save data's world specification failed validation.");

            return WorldLoadValidationResult.Succeeded(result.RepairedSpecification);
        }
    }
}
