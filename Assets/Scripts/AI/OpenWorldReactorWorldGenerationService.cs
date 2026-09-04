using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sim.WorldGeneration.Models;
using UnityEngine;
using UnityEngine.Networking;

namespace Sim.AI
{
    /// <summary>
    /// Real OpenWorld Reactor (reactor.inc) integration — as far as it safely goes in this
    /// phase. OpenWorld Reactor is Reactor, and its "LingBot"/"LingBot World 2" models are
    /// Ant Group models hosted on Reactor's platform (matches this project's original
    /// "Reactor Lingbot" naming exactly). Verified against Reactor's real public documentation
    /// (docs.reactor.inc) and a real, successful test call — see
    /// docs/OPENWORLD_REACTOR_INTEGRATION.md for the full research trail and exactly what was
    /// and wasn't verified.
    ///
    /// What this class actually does for real: exchanges the configured API key for a scoped
    /// session JWT via the verified <c>POST https://api.reactor.inc/tokens</c> endpoint. This
    /// is a genuine network call against the real API, not a simulation.
    ///
    /// What it deliberately does NOT do: open a live LingBot World 2 session and produce a
    /// generated world. That flow is a persistent, continuously-steered video stream (upload a
    /// seed image, set_prompt, start, then drive it live with WASD/camera commands) — a
    /// fundamentally different shape than this interface's one-shot "submit a request, get a
    /// finished result" contract, has no official Unity/C# SDK (only JavaScript/TypeScript and
    /// Python), and its wire transport is not fully documented. Attempting to hand-roll that
    /// blind was explicitly decided against — this is a deliberate stopping point, confirmed
    /// with the user, not an oversight. See docs/OPENWORLD_REACTOR_INTEGRATION.md.
    /// </summary>
    public sealed class OpenWorldReactorWorldGenerationService : IWorldGenerationService
    {
        /// <summary>Verified real endpoint — not user-configurable, since it isn't documented as varying (no staging/regional variant mentioned anywhere in Reactor's docs).</summary>
        public const string TokenEndpoint = "https://api.reactor.inc/tokens";

        /// <summary>Verified real request header name for the API key. Not "Authorization: Bearer" — confirmed from docs.reactor.inc/authentication.md.</summary>
        public const string ApiKeyHeaderName = "Reactor-API-Key";

        /// <summary>Fallback model slug if OPENWORLD_REACTOR_MODEL isn't configured — reactor/lingbot-world-2 is the model this integration was researched and tested against.</summary>
        public const string DefaultModel = "reactor/lingbot-world-2";

        private readonly IReactorCredentialsProvider _credentials;

        public OpenWorldReactorWorldGenerationService(IReactorCredentialsProvider credentials = null)
        {
            _credentials = credentials ?? new EnvironmentReactorCredentialsProvider();
        }

        public async Task<WorldGenerationOutcome> GenerateWorldAsync(WorldGenerationRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
                return WorldGenerationOutcome.Failed(WorldGenerationFailureReason.InvalidResponse, "Request was null.");

            Debug.Log("[WorldGeneration] Prompt received.");
            Debug.Log("[WorldGeneration] Provider: OpenWorld Reactor (reactor.inc)");

            if (!_credentials.TryGetApiKey(out _))
            {
                Debug.LogWarning(
                    "[WorldGeneration] OpenWorld Reactor is not configured — no API key found " +
                    "(checked OPENWORLD_REACTOR_API_KEY environment variable and .env.local).");
                return WorldGenerationOutcome.Failed(WorldGenerationFailureReason.NotConfigured, "OpenWorld Reactor is not configured.");
            }

            string model = _credentials.TryGetModel(out string configuredModel) ? configuredModel : DefaultModel;

            try
            {
                Debug.Log("[WorldGeneration] Generation started.");
                // Real network call — verifies the key is live and Reactor is reachable. This
                // is genuine, not simulated. See class remarks for what happens after this.
                await MintSessionTokenAsync(model, cancellationToken: cancellationToken);
                Debug.Log("[WorldGeneration] Authenticated with OpenWorld Reactor (session token acquired).");
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[WorldGeneration] Generation cancelled.");
                return WorldGenerationOutcome.Failed(WorldGenerationFailureReason.Cancelled, "Generation was cancelled.");
            }
            catch (ReactorNotConfiguredException)
            {
                // Defensive: covered by the TryGetApiKey check above in the normal case, but
                // MintSessionTokenAsync re-checks independently (it's also a public entry
                // point on its own) — this keeps that guarantee true even from here.
                return WorldGenerationOutcome.Failed(WorldGenerationFailureReason.NotConfigured, "OpenWorld Reactor is not configured.");
            }
            catch (ReactorApiException ex)
            {
                // ex.Message is safe to log — see ReactorApiException remarks.
                Debug.LogError($"[WorldGeneration] OpenWorld Reactor request failed: {ex.Message}");
                WorldGenerationFailureReason reason = ex.IsConnectionError
                    ? WorldGenerationFailureReason.NetworkError
                    : WorldGenerationFailureReason.Unavailable;
                return WorldGenerationOutcome.Failed(reason, "OpenWorld Reactor is unavailable.");
            }

            return WorldGenerationOutcome.Failed(
                WorldGenerationFailureReason.NotImplemented,
                "OpenWorld Reactor authentication succeeded, but live session/video generation is not yet implemented. See docs/OPENWORLD_REACTOR_INTEGRATION.md.");
        }

        /// <summary>
        /// Exchanges the configured API key for a scoped session JWT. A real call against the
        /// verified real endpoint/schema — public so it can also serve as a lightweight
        /// "is Reactor configured and reachable" connectivity check independent of the rest of
        /// GenerateWorldAsync's (currently unimplemented) flow.
        /// </summary>
        public async Task<ReactorTokenResult> MintSessionTokenAsync(
            string modelName,
            int maxSessions = 1,
            int maxSessionDurationSeconds = 60,
            int expiresAfterSeconds = 300,
            CancellationToken cancellationToken = default)
        {
            if (!_credentials.TryGetApiKey(out string apiKey))
                throw new ReactorNotConfiguredException();

            string body = BuildTokenRequestBody(modelName, maxSessions, maxSessionDurationSeconds, expiresAfterSeconds);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);

            using var webRequest = new UnityWebRequest(TokenEndpoint, "POST");
            webRequest.uploadHandler = new UploadHandlerRaw(bodyBytes);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");
            // The API key is attached only as this one request header, sent directly to the
            // verified real Reactor endpoint over HTTPS. It is never logged, never included in
            // any exception message, and this method's caller never receives it back.
            webRequest.SetRequestHeader(ApiKeyHeaderName, apiKey);

            UnityWebRequestAsyncOperation operation = webRequest.SendWebRequest();
            while (!operation.isDone)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    webRequest.Abort();
                    throw new OperationCanceledException(cancellationToken);
                }

                await Task.Yield();
            }

            if (webRequest.result == UnityWebRequest.Result.ConnectionError)
                throw new ReactorApiException($"Connection error: {webRequest.error}", isConnectionError: true);

            if (webRequest.result != UnityWebRequest.Result.Success)
                throw new ReactorApiException($"HTTP {webRequest.responseCode}: {webRequest.error}", isConnectionError: false);

            TokenResponseDto parsed;
            try
            {
                parsed = JsonUtility.FromJson<TokenResponseDto>(webRequest.downloadHandler.text);
            }
            catch (Exception ex)
            {
                throw new ReactorApiException($"Could not parse token response: {ex.Message}", isConnectionError: false);
            }

            if (parsed == null || string.IsNullOrEmpty(parsed.jwt))
                throw new ReactorApiException("Token response missing 'jwt' field.", isConnectionError: false);

            return new ReactorTokenResult(parsed.jwt, parsed.expires_at);
        }

        private static string BuildTokenRequestBody(string modelName, int maxSessions, int maxSessionDurationSeconds, int expiresAfterSeconds)
        {
            string escapedModel = EscapeJsonString(modelName);
            return "{\"authorization_details\":[{\"type\":\"session\",\"resources\":{\"models\":{\"match\":[\"" +
                   escapedModel + "\"]}},\"constraints\":{\"max_sessions\":" + maxSessions +
                   ",\"max_session_duration_seconds\":" + maxSessionDurationSeconds + "}}],\"expires_after\":" +
                   expiresAfterSeconds + "}";
        }

        private static string EscapeJsonString(string value) =>
            value.Replace("\\", "\\\\").Replace("\"", "\\\"");

        [Serializable]
        private sealed class TokenResponseDto
        {
            public string jwt;
            public long expires_at;
        }
    }
}
