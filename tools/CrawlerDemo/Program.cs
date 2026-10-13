using HtmlAgilityPack;



if (args.Length == 0)
{
    Console.WriteLine("Usage: CrawlerDemo <url> [maxPages]");
    return;
}

var startUri = new Uri(args[0]);
var maxPages = args.Length > 1 && int.TryParse(args[1], out var mp) ? mp : 20;

using var http = new HttpClient();

// Two data structures to drive the crawler
var toVisit = new Queue<Uri>();
var seen = new HashSet<string>();

// Prime the loop with the start URL
toVisit.Enqueue(startUri);
seen.Add(startUri.AbsoluteUri);


var pagesCrawled = 0;

while (toVisit.Count > 0 && pagesCrawled < maxPages)
{
    var current = toVisit.Dequeue();

    string html;
    try
    {
        var response = await http.GetAsync(current);
        pagesCrawled++;

        Console.WriteLine($"[{pagesCrawled,3}] {(int)response.StatusCode} {current}");

        if (!response.IsSuccessStatusCode) continue;

        html = await response.Content.ReadAsStringAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"      FAILED  {current} -> {ex.Message}");
        continue;

    }

    // Parse and discover internal links
    var doc = new HtmlDocument();
    doc.LoadHtml(html);

    var anchors = doc.DocumentNode.SelectNodes("//a[@href]");
    if (anchors is null) continue;

    foreach (var a in anchors)
    {
        var href = a.GetAttributeValue("href", "");

        if (!Uri.TryCreate(current, href, out var link))
            continue;
        if (link.Scheme != Uri.UriSchemeHttp && link.Scheme != Uri.UriSchemeHttps)
            continue;
        if (link.Host != startUri.Host)          // same-site only
            continue;

        // The dedup gate: HashSet.Add returns false if it was already there.
        if (seen.Add(link.AbsoluteUri)) toVisit.Enqueue(link);
    }
}

Console.WriteLine($"\nDone. Crawled {pagesCrawled} pages. Queue still had {toVisit.Count} waiting.");


