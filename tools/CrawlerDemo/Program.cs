

if (args.Length == 0)
{
    Console.WriteLine("Usage: CrawlerDemo <url>");
    return;
}

var url = args[0];

using var http = new HttpClient();

Console.WriteLine($"Fetching {url}..");

var response = await http.GetAsync(url);
var html = await response.Content.ReadAsStringAsync();


Console.WriteLine($"Status: {(int)response.StatusCode} {response.StatusCode}");
Console.WriteLine($"Content-Type: {response.Content.Headers.ContentType}");
Console.WriteLine($"Body length: {html.Length} characters");
Console.WriteLine("--- first 500 chars ---");
Console.WriteLine(html.Length > 500 ? html.Substring(0, 500) : html);
