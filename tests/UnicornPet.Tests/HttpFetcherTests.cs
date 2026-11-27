using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnicornPet.Core.Updater;
using Xunit;

namespace UnicornPet.Tests
{
    public class HttpFetcherTests
    {
        class Stub : HttpMessageHandler
        {
            public Func<HttpRequestMessage, HttpResponseMessage> Fn;
            public List<string> Urls = new List<string>();
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            {
                Urls.Add(r.RequestUri.ToString());
                return Task.FromResult(Fn(r));
            }
        }

        static HttpResponseMessage Ok(string body) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)) };

        static HttpResponseMessage Redirect(string to, HttpStatusCode code = HttpStatusCode.Found)
        {
            var r = new HttpResponseMessage(code);
            r.Headers.Location = new Uri(to, UriKind.RelativeOrAbsolute);
            return r;
        }

        [Fact] public async Task Fetch_Https_ReturnsBytes()
        {
            var f = new HttpFetcher(new Stub { Fn = r => Ok("hello") });
            Assert.Equal("hello", Encoding.UTF8.GetString(await f.FetchAsync("https://a.example/x", 1000)));
        }

        [Fact] public async Task Fetch_HttpUrl_Throws()
        {
            var s = new Stub { Fn = r => Ok("x") };
            await Assert.ThrowsAnyAsync<Exception>(() => new HttpFetcher(s).FetchAsync("http://a.example/x", 1000));
            Assert.Empty(s.Urls);
        }

        [Fact] public async Task Fetch_RedirectToHttp_Throws()
        {
            var s = new Stub { Fn = r => r.RequestUri.Scheme == "https" ? Redirect("http://evil.example/x") : Ok("pwn") };
            await Assert.ThrowsAnyAsync<Exception>(() => new HttpFetcher(s).FetchAsync("https://a.example/x", 1000));
            Assert.Single(s.Urls);
        }

        [Fact] public async Task Fetch_RedirectToHttps_Follows()
        {
            var s = new Stub { Fn = r => r.RequestUri.Host == "github.com" ? Redirect("https://objects.githubusercontent.com/abc") : Ok("data") };
            var f = new HttpFetcher(s);
            Assert.Equal("data", Encoding.UTF8.GetString(await f.FetchAsync("https://github.com/x/releases/latest/download/m.json", 1000)));
            Assert.Equal(2, s.Urls.Count);
        }

        [Fact] public async Task Fetch_RelativeRedirect_FollowsOnSameHost()
        {
            var s = new Stub { Fn = r => r.RequestUri.AbsolutePath == "/a" ? Redirect("/b") : Ok("ok") };
            Assert.Equal("ok", Encoding.UTF8.GetString(await new HttpFetcher(s).FetchAsync("https://h.example/a", 1000)));
        }

        [Fact] public async Task Fetch_MoreThanFiveRedirects_Throws()
        {
            int n = 0;
            var s = new Stub { Fn = r => Redirect("https://h.example/" + (++n)) };
            await Assert.ThrowsAnyAsync<Exception>(() => new HttpFetcher(s).FetchAsync("https://h.example/0", 1000));
            Assert.True(s.Urls.Count <= 6);
        }

        [Fact] public async Task Fetch_ExactlyFiveRedirects_Succeeds()
        {
            int n = 0;
            var s = new Stub { Fn = r => n < 5 ? Redirect("https://h.example/" + (++n)) : Ok("done") };
            Assert.Equal("done", Encoding.UTF8.GetString(await new HttpFetcher(s).FetchAsync("https://h.example/0", 1000)));
        }

        [Fact] public async Task Fetch_BodyOverLimit_Throws()
        {
            var s = new Stub { Fn = r => Ok(new string('a', 5000)) };
            await Assert.ThrowsAnyAsync<Exception>(() => new HttpFetcher(s).FetchAsync("https://a.example/x", 1000));
        }

        [Fact] public async Task Fetch_NonSuccessStatus_Throws()
        {
            var s = new Stub { Fn = r => new HttpResponseMessage(HttpStatusCode.NotFound) };
            await Assert.ThrowsAnyAsync<Exception>(() => new HttpFetcher(s).FetchAsync("https://a.example/x", 1000));
        }

        [Fact] public async Task Fetch_RedirectWithoutLocation_Throws()
        {
            var s = new Stub { Fn = r => new HttpResponseMessage(HttpStatusCode.Found) };
            await Assert.ThrowsAnyAsync<Exception>(() => new HttpFetcher(s).FetchAsync("https://a.example/x", 1000));
        }
    }
}
