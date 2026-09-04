using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sim.AI.WorldDesign
{
    /// <summary>
    /// Minimal HTTP transport abstraction — introduced Phase 10 specifically so an ILLMClient's
    /// request-building and response-parsing logic (headers, body shape, structured-output
    /// extraction, error-status handling) is unit-testable with a fully in-memory fake, without
    /// ever making a real network call. Deliberately NOT a general networking framework: one
    /// method, POST a JSON body, get a status/body back — the actual transport (real HTTP vs. a
    /// canned test response) is the only thing that varies.
    ///
    /// Cancellation is the ONLY thing this interface's implementations are responsible for
    /// reacting to — no built-in timeout of its own. Timeout-vs-cancellation is deliberately a
    /// caller-side concern (see AnthropicLLMClient's remarks): the caller passes a
    /// CancellationToken already linked to its own timeout, so a real implementation only ever
    /// needs to watch one token and doesn't need to know why it fired.
    /// </summary>
    public interface IHttpTransport
    {
        Task<HttpTransportResponse> PostJsonAsync(
            string url,
            IReadOnlyDictionary<string, string> headers,
            string jsonBody,
            CancellationToken cancellationToken = default);
    }
}
