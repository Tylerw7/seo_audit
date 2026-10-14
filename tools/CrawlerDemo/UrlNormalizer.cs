using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CrawlerDemo
{
    public static class UrlNormalizer
    {
        // Query params that never change which page you land on — pure analytics.
        private static readonly HashSet<string> TrackingParams = new(StringComparer.OrdinalIgnoreCase)
        {
            "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content",
            "gclid", "fbclid", "msclkid", "mc_cid", "mc_eid", "ref",
        };

        // Returns a canonical absolute Uri, or null if the input isn't a usable http(s) URL.
        public static Uri? Normalize(string url, Uri? baseUri = null)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            // Resolve relative links ("/about", "../x") against the page they were found on.
            Uri? uri;
            if (baseUri is not null)
            {
                if (!Uri.TryCreate(baseUri, url, out uri)) return null;
            }
            else if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                return null;
            }

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return null;

            var builder = new UriBuilder(uri)
            {
                Host = uri.Host.ToLowerInvariant(),   // Site.com -> site.com
                Fragment = string.Empty,              // drop #section
            };

            // Drop the port if it's the scheme default (http:80 / https:443).
            if ((uri.Scheme == Uri.UriSchemeHttp && uri.Port == 80) ||
                (uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443))
            {
                builder.Port = -1;
            }

            builder.Path = NormalizePath(uri.AbsolutePath);
            builder.Query = NormalizeQuery(uri.Query);

            return builder.Uri;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "/";
            if (path.Length > 1 && path.EndsWith('/'))   // "/about/" -> "/about", but keep "/"
                path = path.TrimEnd('/');
            return path.Length == 0 ? "/" : path;
        }

        private static string NormalizeQuery(string query)
        {
            if (string.IsNullOrEmpty(query) || query == "?") return string.Empty;

            var trimmed = query.StartsWith('?') ? query[1..] : query;

            var kept = trimmed
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(pair => !TrackingParams.Contains(pair.Split('=', 2)[0]))
                .OrderBy(pair => pair, StringComparer.Ordinal)   // stable order: ?b=2&a=1 -> ?a=1&b=2
                .ToList();

            return kept.Count == 0 ? string.Empty : string.Join('&', kept);
        }
    }
}