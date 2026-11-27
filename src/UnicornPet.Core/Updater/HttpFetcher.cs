using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace UnicornPet.Core.Updater
{
    /// <summary>Fetches a URL under a byte cap and an overall deadline. Implemented by HttpFetcher; tests supply fakes.</summary>
    public delegate Task<byte[]> FetchFunc(string url, long maxBytes, TimeSpan deadline);

    /// <summary>
    /// HTTPS-only GET. Redirects are followed by hand (https hops only, max 5, and every hop must pass the same URL rules as the first).
    /// One overall deadline covers the redirects, the headers and every body read; the deadline is checked before each stage starts, and
    /// the size cap is enforced while streaming with one sentinel byte, so at most cap + 1 bytes are ever read.
    /// Every wait is raced against the deadline and ABANDONED when it runs out, because some runtimes do not stop a blocked read when
    /// cancellation is requested. An abandoned stage that later finishes has its response, stream and request disposed (and any fault
    /// observed) by a continuation, so nothing is leaked and nothing crashes the process.
    /// </summary>
    public class HttpFetcher
    {
        public const int MaxRedirects = 5;
        public static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(30);
        readonly HttpClient _client;

        public HttpFetcher(HttpMessageHandler handler = null)
        {
            _client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
            _client.Timeout = Timeout.InfiniteTimeSpan; // the per-call deadline below is the only timeout
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("UnicornPet/1.0");
        }

        public async Task<byte[]> FetchAsync(string url, long maxBytes, TimeSpan? deadline = null)
        {
            if (!UrlRules.IsAllowed(url)) throw new InvalidOperationException("Only https URLs are allowed");
            var uri = new Uri(url.Trim());
#if DEBUG
            if (uri.Scheme == Uri.UriSchemeFile)
            {
                var fi = new FileInfo(uri.LocalPath);
                if (fi.Length > maxBytes) throw new InvalidDataException("too large");
                return File.ReadAllBytes(fi.FullName);
            }
#endif
            var total = deadline ?? DefaultDeadline;
            var clock = Stopwatch.StartNew();
            using (var cts = new CancellationTokenSource(total))
            {
                var ct = cts.Token;
                for (int hop = 0; ; hop++)
                {
                    HttpRequestMessage req = null;
                    HttpResponseMessage resp = null;
                    Stream stream = null;
                    bool abandoned = false;
                    try
                    {
                        CheckTime(total, clock);
                        req = new HttpRequestMessage(HttpMethod.Get, uri);
                        var sending = req;
                        resp = await Within(_client.SendAsync(sending, HttpCompletionOption.ResponseHeadersRead, ct), total - clock.Elapsed,
                            late => { try { late.Dispose(); } catch { } try { sending.Dispose(); } catch { } }).ConfigureAwait(false);

                        int code = (int)resp.StatusCode;
                        if (code == 301 || code == 302 || code == 303 || code == 307 || code == 308)
                        {
                            if (hop >= MaxRedirects) throw new InvalidOperationException("Too many redirects");
                            var loc = resp.Headers.Location;
                            if (loc == null) throw new InvalidOperationException("Redirect without Location");
                            var next = loc.IsAbsoluteUri ? loc : new Uri(uri, loc);
                            if (next.Scheme != Uri.UriSchemeHttps || !UrlRules.IsAllowed(next.AbsoluteUri)) throw new InvalidOperationException("Redirect target refused");
                            uri = next;
                            continue;
                        }
                        if (!resp.IsSuccessStatusCode) throw new HttpRequestException("HTTP " + code);
                        var len = resp.Content.Headers.ContentLength;
                        if (len.HasValue && len.Value > maxBytes) throw new InvalidDataException("too large");

                        CheckTime(total, clock);
                        stream = await Within(resp.Content.ReadAsStreamAsync(), total - clock.Elapsed, late => { try { late.Dispose(); } catch { } }).ConfigureAwait(false);
                        using (var ms = new MemoryStream())
                        {
                            var buf = new byte[81920];
                            while (true)
                            {
                                CheckTime(total, clock);
                                // never ask for more than what is left under the cap plus ONE sentinel byte: at most cap + 1 bytes are read
                                int want = (int)Math.Min(buf.Length, maxBytes - ms.Length + 1);
                                int n = await Within(stream.ReadAsync(buf, 0, want, ct), total - clock.Elapsed, null).ConfigureAwait(false);
                                if (n <= 0) break;
                                if (ms.Length + n > maxBytes) throw new InvalidDataException("too large");
                                ms.Write(buf, 0, n);
                            }
                            return ms.ToArray();
                        }
                    }
                    catch (TimeoutException)
                    {
                        abandoned = true;
                        throw;
                    }
                    catch (Exception ex) when (ct.IsCancellationRequested && (ex is OperationCanceledException || ex is ObjectDisposedException || ex is IOException || ex is HttpRequestException))
                    {
                        abandoned = true;
                        throw new TimeoutException("The request did not finish before its deadline");
                    }
                    finally
                    {
                        if (abandoned)
                        {
                            var q = req; var r = resp; var s = stream;
                            var ignored = Task.Run(() => { try { s?.Dispose(); } catch { } try { r?.Dispose(); } catch { } try { q?.Dispose(); } catch { } });
                        }
                        else
                        {
                            try { stream?.Dispose(); } catch { }
                            try { resp?.Dispose(); } catch { }
                            try { req?.Dispose(); } catch { }
                        }
                    }
                }
            }
        }

        static void CheckTime(TimeSpan total, Stopwatch clock)
        {
            if (total - clock.Elapsed <= TimeSpan.Zero) throw new TimeoutException("The request did not finish before its deadline");
        }

        /// <summary>
        /// Waits for the task but never longer than the time left. A task that outlives the deadline is abandoned, not awaited: if it
        /// later completes, <paramref name="disposeLate"/> gets its result; if it faults, the fault is observed.
        /// </summary>
        static async Task<T> Within<T>(Task<T> task, TimeSpan remaining, Action<T> disposeLate)
        {
            if (task.IsCompleted) return await task.ConfigureAwait(false);
            if (remaining <= TimeSpan.Zero) { Abandon(task, disposeLate); throw new TimeoutException("The request did not finish before its deadline"); }
            using (var stopDelay = new CancellationTokenSource())
            {
                var delay = Task.Delay(remaining, stopDelay.Token);
                var done = await Task.WhenAny(task, delay).ConfigureAwait(false);
                if (done == task)
                {
                    stopDelay.Cancel();
                    return await task.ConfigureAwait(false);
                }
            }
            Abandon(task, disposeLate);
            throw new TimeoutException("The request did not finish before its deadline");
        }

        static void Abandon<T>(Task<T> task, Action<T> disposeLate)
        {
            task.ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion)
                {
                    try { disposeLate?.Invoke(t.Result); } catch { }
                }
                else
                {
                    var ignored = t.Exception; // observed, so a late failure cannot surface as an unobserved exception
                }
            }, TaskScheduler.Default);
        }
    }
}
