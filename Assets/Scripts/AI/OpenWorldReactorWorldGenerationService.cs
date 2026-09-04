using System.Threading;
using System.Threading.Tasks;
using Sim.WorldGeneration.Models;
using UnityEngine;

namespace Sim.AI
{
    /// <summary>
    /// Intended real backend, currently a documented stub. This project's development
    /// environment has no OpenWorld Reactor SDK, API, configuration, or documentation
    /// available to inspect (checked: environment variables, installed CLI tools,
    /// npm/pip/gem packages, common config file locations — none found). Rather than invent
    /// an API shape, this class is written to fail loudly and explain exactly what's missing,
    /// so a caller can choose to fall back to MockWorldGenerationService.
    ///
    /// To complete this integration once real access exists, see docs/WORLD_SPECIFICATION.md
    /// "Open questions" for the full checklist. In short, this needs to learn:
    ///   - how a prompt is actually submitted to Reactor (REST call? SDK/plugin? local process?)
    ///   - authentication (the REACTOR_API_KEY / REACTOR_ENDPOINT / REACTOR_MODEL environment
    ///     variable names below are placeholders, not confirmed against any real contract)
    ///   - the real shape of what it returns (structured data? a scene/asset reference? both?
    ///     — see ReactorWorldResult.PayloadKind) so ReactorWorldAdapter can be written for real
    ///   - whether/how it supports seeds and deterministic regeneration
    ///
    /// Never commit real credentials here or anywhere in this repository.
    /// </summary>
    public sealed class OpenWorldReactorWorldGenerationService : IWorldGenerationService
    {
        public const string ApiKeyEnvironmentVariable = "REACTOR_API_KEY";
        public const string EndpointEnvironmentVariable = "REACTOR_ENDPOINT";
        public const string ModelEnvironmentVariable = "REACTOR_MODEL";

        // Throws synchronously rather than returning a faulted Task — intentional, not an
        // oversight: this is a "cannot proceed at all" guard (nothing has been requested over
        // any transport yet), the same category as validating arguments before doing async
        // work, which is conventionally thrown synchronously even from an async-signature
        // method (see Stephen Toub's guidance on Task-returning method exceptions).
        public Task<WorldGenerationOutcome> GenerateWorldAsync(WorldGenerationRequest request, CancellationToken cancellationToken = default)
        {
            Debug.LogWarning(
                "OpenWorldReactorWorldGenerationService: not configured — no OpenWorld Reactor " +
                "SDK/API access exists in this project yet. See docs/WORLD_SPECIFICATION.md.");

            throw new ReactorNotConfiguredException();
        }
    }
}
