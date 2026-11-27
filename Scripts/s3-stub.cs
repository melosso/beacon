#!/usr/bin/env dotnet
#:sdk Microsoft.NET.Sdk.Web
#:property PublishAot=false

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

const string AccessKey = "beacon-dev";
const string SecretKey = "beacon-dev-secret";
var bucket = Environment.GetEnvironmentVariable("S3_STUB_BUCKET") ?? "beacon";
var port = int.TryParse(Environment.GetEnvironmentVariable("S3_STUB_PORT"), out var p) ? p : 9556;
var root = Path.GetFullPath(Environment.GetEnvironmentVariable("S3_STUB_DATA") ?? Path.Combine(Path.GetTempPath(), "beacon-s3-stub"));
var endpoint = $"http://127.0.0.1:{port}";
Directory.CreateDirectory(root);

Debug.Assert(Encoding.ASCII.GetString(DecodeAwsChunked("5;chunk-signature=a\r\nhello\r\n0;chunk-signature=b\r\nx-amz-checksum-crc32:x\r\n\r\n"u8.ToArray())) == "hello");

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(endpoint);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var app = builder.Build();

app.MapMethods("/{name}", ["GET", "HEAD"], (string name, HttpRequest request) =>
{
    if (name != bucket) return Error(404, "NoSuchBucket");
    if (!request.Query.ContainsKey("location") && !HttpMethods.IsHead(request.Method)) return Error(400, "NotImplemented");
    return Results.Content("""<?xml version="1.0" encoding="UTF-8"?><LocationConstraint xmlns="http://s3.amazonaws.com/doc/2006-03-01/"/>""", "application/xml");
});

app.MapPut("/{name}/{**key}", async Task<IResult> (string name, string key, HttpContext context) =>
{
    var request = context.Request;
    if (!Signed(request)) return Error(403, "InvalidAccessKeyId");
    if (name != bucket) return Error(404, "NoSuchBucket");

    using var buffer = new MemoryStream();
    await request.Body.CopyToAsync(buffer);
    var body = buffer.ToArray();
    var chunked = request.Headers.ContentEncoding.ToString().Contains("aws-chunked")
        || request.Headers["x-amz-content-sha256"].ToString().StartsWith("STREAMING-", StringComparison.Ordinal);
    if (chunked) body = DecodeAwsChunked(body);
    if (long.TryParse(request.Headers["x-amz-decoded-content-length"], out var expected) && expected != body.Length)
        return Error(400, "IncompleteBody");

    var path = PathFor(key);
    await File.WriteAllBytesAsync(path, body);
    await File.WriteAllTextAsync(path + ".type", request.ContentType ?? "application/octet-stream");
    Console.WriteLine($"PUT {key} ({body.Length} bytes)");
    context.Response.Headers.ETag = ETag(body);
    return Results.Ok();
});

app.MapMethods("/{name}/{**key}", ["GET", "HEAD"], async Task<IResult> (string name, string key, HttpContext context) =>
{
    var path = PathFor(key);
    if (name != bucket || !File.Exists(path)) return Error(404, "NoSuchKey");

    var body = await File.ReadAllBytesAsync(path);
    context.Response.Headers.LastModified = File.GetLastWriteTimeUtc(path).ToString("R");
    context.Response.Headers.ETag = ETag(body);
    context.Response.Headers.AccessControlAllowOrigin = "*";
    return Results.Bytes(body, await File.ReadAllTextAsync(path + ".type"));
});

app.MapDelete("/{name}/{**key}", (string name, string key, HttpRequest request) =>
{
    if (!Signed(request)) return Error(403, "InvalidAccessKeyId");
    var path = PathFor(key);
    File.Delete(path);
    File.Delete(path + ".type");
    Console.WriteLine($"DELETE {key}");
    return Results.NoContent();
});

Console.WriteLine($"""
    S3 stub on {endpoint}, files in {root}

    Set it in Beacon under Settings, Integration, Object storage:
      Provider    MinIO
      Endpoint    {endpoint}
      Bucket      {bucket}
      Region      us-east-1
      Access key  {AccessKey}
      Secret key  {SecretKey}
      Public URL  {endpoint}/{bucket}

    Signatures are not verified. Press Ctrl+C to stop.
    """);
app.Run();

string PathFor(string key) => Path.Combine(root, Convert.ToBase64String(Encoding.UTF8.GetBytes(key)).Replace('/', '_').Replace('+', '-'));

static bool Signed(HttpRequest request)
{
    var authorization = request.Headers.Authorization.ToString();
    if (authorization.Contains($"Credential={AccessKey}/", StringComparison.Ordinal)) return true;
    Console.WriteLine($"Refused {request.Method} {request.Path}: access key is not {AccessKey} ({authorization.Split(',')[0]})");
    return false;
}

static string ETag(byte[] body) => $"\"{Convert.ToHexStringLower(MD5.HashData(body))}\"";

static IResult Error(int status, string code) => Results.Content(
    $"""<?xml version="1.0" encoding="UTF-8"?><Error><Code>{code}</Code><Message>{code}</Message></Error>""",
    "application/xml", statusCode: status);

static byte[] DecodeAwsChunked(byte[] body)
{
    using var output = new MemoryStream();
    var position = 0;
    while (true)
    {
        var lineEnd = body.AsSpan(position).IndexOf("\r\n"u8) + position;
        var size = Convert.ToInt32(Encoding.ASCII.GetString(body, position, lineEnd - position).Split(';')[0], 16);
        position = lineEnd + 2;
        if (size == 0) return output.ToArray();
        output.Write(body, position, size);
        position += size + 2;
    }
}
