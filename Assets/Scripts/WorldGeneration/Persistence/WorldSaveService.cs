using System;
using System.IO;
using System.Text.RegularExpressions;
using Sim.WorldGeneration.Validation;
using UnityEngine;

namespace Sim.WorldGeneration.Persistence
{
    /// <summary>
    /// The real, file-backed IWorldSaveService — writes under <see cref="Application.
    /// persistentDataPath"/> (Unity's own application-controlled, per-platform-appropriate
    /// storage location), never into Assets/ProjectSettings/Packages/the repository itself.
    ///
    /// Path traversal is prevented structurally, not by blacklisting "..": <see cref="SlotNamePattern"/>
    /// is a strict allow-list (letters, digits, underscore, hyphen only — no dot, no slash, no
    /// backslash, no tilde at all), so a slot name can never contain a path separator or a
    /// parent-directory segment in the first place. The one default slot ("default") always
    /// satisfies it; a caller-supplied slot name that doesn't is rejected outright, never
    /// sanitized-and-used.
    ///
    /// Every file operation is wrapped against IOException/UnauthorizedAccessException and
    /// reported as a clean WorldSaveOperationResult/WorldLoadResult failure — never an uncaught
    /// exception, never a crash.
    /// </summary>
    public sealed class WorldSaveService : IWorldSaveService
    {
        public const string DefaultSlotName = "default";
        private const string SaveDirectoryName = "Saves";

        private static readonly Regex SlotNamePattern = new Regex(@"^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled);

        private readonly string _rootDirectory;
        private readonly IWorldSaveSerializer _serializer;
        private readonly IWorldSpecificationValidator _specificationValidator;

        /// <param name="rootDirectory">Bypasses Application.persistentDataPath — for tests, so they never read/write the machine's real save directory. Leave null for normal use.</param>
        /// <param name="serializer">Bypasses WorldSaveJsonSerializer — for tests. Leave null for normal use.</param>
        /// <param name="specificationValidator">Bypasses a fresh WorldSpecificationValidator — for tests that want to inject a fake. Leave null for normal use.</param>
        public WorldSaveService(string rootDirectory = null, IWorldSaveSerializer serializer = null, IWorldSpecificationValidator specificationValidator = null)
        {
            _rootDirectory = rootDirectory ?? Application.persistentDataPath;
            _serializer = serializer ?? new WorldSaveJsonSerializer();
            _specificationValidator = specificationValidator ?? new WorldSpecificationValidator();
        }

        public WorldSaveOperationResult Save(WorldSaveData data, string slotName = null)
        {
            if (data == null)
                return WorldSaveOperationResult.Failed("No save data was provided.");

            if (!TryResolvePath(slotName, out string path, out string pathError))
                return WorldSaveOperationResult.Failed(pathError);

            string json;
            try
            {
                json = _serializer.Serialize(data);
            }
            catch (Exception ex)
            {
                return WorldSaveOperationResult.Failed($"Failed to serialize save data: {ex.Message}");
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, json);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return WorldSaveOperationResult.Failed($"Failed to write save file: {ex.Message}");
            }

            return WorldSaveOperationResult.Succeeded();
        }

        public WorldLoadResult Load(string slotName = null)
        {
            if (!TryResolvePath(slotName, out string path, out string pathError))
                return WorldLoadResult.Failed(pathError);

            if (!File.Exists(path))
                return WorldLoadResult.Failed("No save file exists.");

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return WorldLoadResult.Failed($"Failed to read save file: {ex.Message}");
            }

            WorldSaveDeserializeResult deserialized = _serializer.Deserialize(json);
            if (!deserialized.Success)
                return WorldLoadResult.Failed($"Save file is corrupted: {deserialized.ErrorMessage}");

            WorldLoadValidationResult validation = WorldSaveValidator.Validate(deserialized.Data, _specificationValidator);
            if (!validation.Success)
                return WorldLoadResult.Failed($"Save data failed validation: {validation.ErrorMessage}");

            // Use the (possibly-repaired) validated specification, not the raw deserialized one —
            // exactly what WorldGenerator must be given, matching every other caller of
            // WorldSpecificationValidator in this project.
            deserialized.Data.Specification = validation.ValidatedSpecification;
            return WorldLoadResult.Succeeded(deserialized.Data);
        }

        public bool Delete(string slotName = null)
        {
            if (!TryResolvePath(slotName, out string path, out _) || !File.Exists(path))
                return false;

            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        public bool Exists(string slotName = null) =>
            TryResolvePath(slotName, out string path, out _) && File.Exists(path);

        /// <summary>The one place a slot name becomes a filesystem path — every other member goes through this, so the allow-list check can never be bypassed.</summary>
        private bool TryResolvePath(string slotName, out string path, out string error)
        {
            string resolvedSlot = string.IsNullOrEmpty(slotName) ? DefaultSlotName : slotName;

            if (!SlotNamePattern.IsMatch(resolvedSlot))
            {
                path = null;
                error = "Invalid save slot name.";
                return false;
            }

            path = Path.Combine(_rootDirectory, SaveDirectoryName, resolvedSlot + ".json");
            error = null;
            return true;
        }
    }
}
