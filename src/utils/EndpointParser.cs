using System;

namespace Utils;

public static class EndpointParser
{
    public readonly record struct Endpoint(string Host, int Port);

    /// <summary>
    /// Parses host:port using System.Uri (IPv4, IPv6 bracketed, hostnames).
    /// </summary>
    public static bool TryParse(
        string raw,
        out Endpoint endpoint,
        int defaultPort = 7000
    )
    {
        endpoint = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        raw = raw.Trim();
        if (!raw.Contains(':'))
        {
            raw = $"{raw}:{defaultPort}";
        }

        if (!Uri.TryCreate($"http://{raw}", UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (string.IsNullOrEmpty(uri.Host))
        {
            return false;
        }

        var port = uri.Port > 0 ? uri.Port : defaultPort;
        if (port is < 1 or > 65535)
        {
            return false;
        }

        endpoint = new Endpoint(uri.Host, port);
        return true;
    }
}
