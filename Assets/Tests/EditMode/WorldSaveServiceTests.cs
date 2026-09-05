using System;
using System.IO;
using NUnit.Framework;
using Sim.WorldGeneration.Models;
using Sim.WorldGeneration.Persistence;
using UnityEngine;

namespace Sim.Tests.EditMode
{
    /// <summary>
    /// WorldSaveService tests use an isolated temp directory (never the machine's real
    /// Application.persistentDataPath) created fresh per test and deleted in TearDown — per this
    /// phase's explicit "use an isolated temporary/test-controlled location" instruction.
    /// </summary>
    public class WorldSaveServiceTests
    {
        private string _tempRoot;
        private WorldSaveService _service;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "sim_world_save_tests_" + Guid.NewGuid().ToString("N"));
            _service = new WorldSaveService(_tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }

        private static WorldSpecification ValidSpecification() => new WorldSpecification
        {
            OriginalPrompt = "Create a small test course.",
            Seed = 42,
            Terrain = new TerrainSpecification { TerrainType = "hills", Width = 200f, Depth = 200f, MaxHeight = 40f },
            Spawn = new SpawnSpecification { Position = new Vector3(0f, 25f, 0f) }
        };

        // ------------------------------------------------------------------
        // Round trip
        // ------------------------------------------------------------------

        [Test]
        public void Save_ThenLoad_RoundTripsPromptSeedAndSpecification()
        {
            WorldSaveData data = WorldSaveData.FromSpecification(ValidSpecification());

            WorldSaveOperationResult saveResult = _service.Save(data);
            Assert.IsTrue(saveResult.Success, saveResult.ErrorMessage);

            WorldLoadResult loadResult = _service.Load();
            Assert.IsTrue(loadResult.Success, loadResult.ErrorMessage);
            Assert.AreEqual(data.Prompt, loadResult.Data.Prompt);
            Assert.AreEqual(data.Seed, loadResult.Data.Seed);
            Assert.AreEqual(data.Specification.Terrain.TerrainType, loadResult.Data.Specification.Terrain.TerrainType);
        }

        [Test]
        public void Save_WritesUnderTheConfiguredRoot_NotElsewhere()
        {
            _service.Save(WorldSaveData.FromSpecification(ValidSpecification()));

            string expectedPath = Path.Combine(_tempRoot, "Saves", WorldSaveService.DefaultSlotName + ".json");
            Assert.IsTrue(File.Exists(expectedPath));
        }

        // ------------------------------------------------------------------
        // Path traversal / invalid slot names
        // ------------------------------------------------------------------

        [Test]
        public void Save_PathTraversalSlotName_Rejected()
        {
            WorldSaveOperationResult result = _service.Save(WorldSaveData.FromSpecification(ValidSpecification()), "../escape");
            Assert.IsFalse(result.Success);
        }

        [Test]
        public void Save_ParentDirectorySlotName_DoesNotEscapeSavesDirectory()
        {
            _service.Save(WorldSaveData.FromSpecification(ValidSpecification()), "../../escape");

            // Whether rejected outright or (defensively) written, nothing must appear outside
            // the configured root directory.
            string escapedPath = Path.Combine(Path.GetDirectoryName(_tempRoot), "escape.json");
            Assert.IsFalse(File.Exists(escapedPath));
        }

        [Test]
        public void Save_AbsolutePathSlotName_Rejected()
        {
            string absolute = Path.Combine(Path.GetTempPath(), "absolute_attempt");
            WorldSaveOperationResult result = _service.Save(WorldSaveData.FromSpecification(ValidSpecification()), absolute);

            Assert.IsFalse(result.Success);
        }

        [Test]
        public void Save_SlotNameWithForwardSlash_Rejected()
        {
            Assert.IsFalse(_service.Save(WorldSaveData.FromSpecification(ValidSpecification()), "sub/slot").Success);
        }

        [Test]
        public void Save_SlotNameWithBackslash_Rejected()
        {
            Assert.IsFalse(_service.Save(WorldSaveData.FromSpecification(ValidSpecification()), "sub\\slot").Success);
        }

        [Test]
        public void Load_PathTraversalSlotName_Rejected()
        {
            Assert.IsFalse(_service.Load("../escape").Success);
        }

        [Test]
        public void Save_ValidAlphanumericSlotName_Succeeds()
        {
            Assert.IsTrue(_service.Save(WorldSaveData.FromSpecification(ValidSpecification()), "slot_2").Success);
        }

        // ------------------------------------------------------------------
        // Missing / corrupted files
        // ------------------------------------------------------------------

        [Test]
        public void Load_NoSaveFileExists_FailsCleanly_DoesNotThrow()
        {
            WorldLoadResult result = null;
            Assert.DoesNotThrow(() => result = _service.Load());
            Assert.IsFalse(result.Success);
        }

        [Test]
        public void Load_CorruptedSaveFile_FailsCleanly_DoesNotThrow()
        {
            string path = Path.Combine(_tempRoot, "Saves", WorldSaveService.DefaultSlotName + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "{ not actually valid json ]]");

            WorldLoadResult result = null;
            Assert.DoesNotThrow(() => result = _service.Load());
            Assert.IsFalse(result.Success);
        }

        [Test]
        public void Load_SaveFileFailsSpecificationValidation_FailsCleanly()
        {
            string path = Path.Combine(_tempRoot, "Saves", WorldSaveService.DefaultSlotName + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            // Valid JSON, valid envelope shape, but no OriginalPrompt in the specification —
            // the one WorldSpecificationValidator error that's genuinely unrecoverable.
            File.WriteAllText(path, "{\"Version\":1,\"Prompt\":\"\",\"Seed\":1,\"Specification\":{\"OriginalPrompt\":\"\"}}");

            WorldLoadResult result = _service.Load();
            Assert.IsFalse(result.Success);
        }

        // ------------------------------------------------------------------
        // Exists / Delete
        // ------------------------------------------------------------------

        [Test]
        public void Exists_NoSaveYet_False()
        {
            Assert.IsFalse(_service.Exists());
        }

        [Test]
        public void Exists_AfterSave_True()
        {
            _service.Save(WorldSaveData.FromSpecification(ValidSpecification()));
            Assert.IsTrue(_service.Exists());
        }

        [Test]
        public void Delete_RemovesTheSaveFile()
        {
            _service.Save(WorldSaveData.FromSpecification(ValidSpecification()));
            Assert.IsTrue(_service.Delete());
            Assert.IsFalse(_service.Exists());
        }

        [Test]
        public void Delete_NothingToDelete_ReturnsFalse_DoesNotThrow()
        {
            bool result = true;
            Assert.DoesNotThrow(() => result = _service.Delete());
            Assert.IsFalse(result);
        }
    }
}
