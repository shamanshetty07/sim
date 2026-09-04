using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Sim.AI.WorldDesign
{
    /// <summary>
    /// The real IHttpTransport — POSTs via UnityWebRequest. Polling pattern (SendWebRequest,
    /// then `while (!operation.isDone) { check cancellation; await Task.Yield(); }`) is copied
    /// deliberately from OpenWorldReactorWorldGenerationService.MintSessionTokenAsync (Phase 6),
    /// the project's one existing verified-working real HTTP call from Unity: it never blocks
    /// the calling thread (no Thread.Sleep, no blocking .Result/.Wait()), and Task.Yield()
    /// returns control to Unity's SynchronizationContext each spin rather than busy-looping —
    /// safe to call from the main thread, which is the only thread this is ever called from.
    /// </summary>
    public sealed class UnityWebRequestHttpTransport : IHttpTransport
    {
        public async Task<HttpTransportResponse> PostJsonAsync(
            string url,
            IReadOnlyDictionary<string, string> headers,
            string jsonBody,
            CancellationToken cancellationToken = default)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody ?? string.Empty);

            using var webRequest = new UnityWebRequest(url, "POST");
            webRequest.uploadHandler = new UploadHandlerRaw(bodyBytes);
            webRequest.downloadHandler = new DownloadHandlerBuffer();

            if (headers != null)
            {
                foreach (KeyValuePair<string, string> header in headers)
                    webRequest.SetRequestHeader(header.Key, header.Value);
            }

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
                return HttpTransportResponse.ConnectionError(webRequest.error);

            // ProtocolError (a non-2xx HTTP status) still has a real body/status to read — only
            // a genuine connection-level failure above skips straight to ConnectionError.
            return HttpTransportResponse.Completed(webRequest.responseCode, webRequest.downloadHandler.text);
        }
    }
}
