using HtmlAgilityPack;



if (args.Length == 0)
{
    Console.WriteLine("Usage: CrawlerDemo <url>");
    return;
}

var url = args[0];
var pageUri = new Uri(url);

using var http = new HttpClient();

Console.WriteLine($"Fetching {url}..");

var response = await http.GetAsync(url);
var html = await response.Content.ReadAsStringAsync();
Console.WriteLine($"Status: {(int)response.StatusCode} Body: {html.Length} chars");

// -- Parse the html into a queryable tree --
var doc = new HtmlDocument();
doc.LoadHtml(html);

// Find every <a> element that has an href attribute.
var anchors = doc.DocumentNode.SelectNodes("//a[@href]");

if (anchors is null)
{
    Console.WriteLine("No links found.");
    return;
}

Console.WriteLine($"\nFound {anchors.Count} <a href> tags. Resolving...\n");

foreach (var a in anchors)
{
    var href = a.GetAttributeValue("href", "");

    // Turn whatever is in href into an absolute URL, relative to this page.
    if (!Uri.TryCreate(pageUri, href, out var absolute))
        continue;

    // We only care about http/https links (skip mailto:, tel:, javascript:, #...)
    if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps)
        continue;

    var sameSite = absolute.Host == pageUri.Host ? "internal" : "external";
    Console.WriteLine($"[{sameSite}] {absolute}");
}

