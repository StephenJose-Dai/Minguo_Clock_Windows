using System.Net;
using System.Net.Http.Headers;

namespace Minguo.Core;

/// <summary>网络请求：统一超时、浏览器 UA、自动解压</summary>
public static class Net
{
    private static readonly HttpClient Client = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        AllowAutoRedirect = true,
    })
    {
        Timeout = Timeout.InfiniteTimeSpan, // 每次请求单独控制超时
    };

    static Net()
    {
        Client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
    }

    public static async Task<string> GetStringAsync(string url, IDictionary<string, string>? headers = null, int timeoutMs = 10_000, CancellationToken ct = default)
    {
        using var req = Build(url, headers);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        using var res = await Client.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
    }

    public static async Task<byte[]> GetBytesAsync(string url, int timeoutMs = 30_000, CancellationToken ct = default)
    {
        using var req = Build(url, null);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        using var res = await Client.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
    }

    private static HttpRequestMessage Build(string url, IDictionary<string, string>? headers)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (headers != null)
            foreach (var (k, v) in headers)
                req.Headers.TryAddWithoutValidation(k, v);
        return req;
    }
}
