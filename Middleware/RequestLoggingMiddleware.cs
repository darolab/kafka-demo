using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KafkaDemo.Middleware;

public sealed class RequestLoggingMiddleware
{
    private const int MaxLoggedBodyBytes = 8 * 1024;
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var requestBody = await ReadRequestBodyAsync(context);
        var originalResponseBody = context.Response.Body;
        var responseCapture = new LimitedCaptureStream(
            originalResponseBody,
            MaxLoggedBodyBytes);
        var stopwatch = Stopwatch.StartNew();
        Exception? failure = null;

        context.Response.Body = responseCapture;
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            stopwatch.Stop();
            context.Response.Body = originalResponseBody;

            var responseBody = FormatJsonBody(
                responseCapture.CapturedBytes,
                context.Response.ContentType,
                responseCapture.IsTruncated);

            if (failure is null)
            {
                _logger.LogInformation(
                    "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {ElapsedMilliseconds}ms. Request body: {RequestBody}; Response body: {ResponseBody}",
                    context.Request.Method,
                    context.Request.Path,
                    context.Response.StatusCode,
                    stopwatch.ElapsedMilliseconds,
                    requestBody,
                    responseBody);
            }
            else
            {
                _logger.LogError(
                    failure,
                    "HTTP {RequestMethod} {RequestPath} failed after {ElapsedMilliseconds}ms. Request body: {RequestBody}; Response body: {ResponseBody}",
                    context.Request.Method,
                    context.Request.Path,
                    stopwatch.ElapsedMilliseconds,
                    requestBody,
                    responseBody);
            }
        }
    }

    private static async Task<string?> ReadRequestBodyAsync(HttpContext context)
    {
        if (!IsJson(context.Request.ContentType))
        {
            return null;
        }

        context.Request.EnableBuffering();
        var originalPosition = context.Request.Body.Position;
        var buffer = new byte[MaxLoggedBodyBytes + 1];
        var bytesRead = 0;

        try
        {
            while (bytesRead < buffer.Length)
            {
                var read = await context.Request.Body.ReadAsync(
                    buffer.AsMemory(bytesRead, buffer.Length - bytesRead),
                    context.RequestAborted);
                if (read == 0)
                {
                    break;
                }

                bytesRead += read;
            }
        }
        finally
        {
            context.Request.Body.Position = originalPosition;
        }

        return FormatJsonBody(
            buffer.AsSpan(0, Math.Min(bytesRead, MaxLoggedBodyBytes)).ToArray(),
            context.Request.ContentType,
            bytesRead > MaxLoggedBodyBytes);
    }

    private static string? FormatJsonBody(
        byte[] bytes,
        string? contentType,
        bool truncated)
    {
        if (bytes.Length == 0)
        {
            return null;
        }

        if (!IsJson(contentType))
        {
            return "[body omitted: content is not JSON]";
        }

        if (truncated)
        {
            return $"[body omitted: exceeds {MaxLoggedBodyBytes} bytes]";
        }

        try
        {
            var json = JsonNode.Parse(bytes);
            RedactSensitiveProperties(json);
            return json?.ToJsonString() ?? "null";
        }
        catch (JsonException)
        {
            return "[body omitted: invalid JSON]";
        }
    }

    private static bool IsJson(string? contentType)
    {
        var mediaType = contentType?.Split(';', 2)[0].Trim();
        return mediaType is not null &&
            (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
             mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
    }

    private static void RedactSensitiveProperties(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToArray())
            {
                if (IsSensitiveProperty(property.Key))
                {
                    obj[property.Key] = "[REDACTED]";
                }
                else
                {
                    RedactSensitiveProperties(property.Value);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                RedactSensitiveProperties(item);
            }
        }
    }

    private static bool IsSensitiveProperty(string name)
    {
        var normalized = name.Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .ToLowerInvariant();

        return normalized.Contains("password", StringComparison.Ordinal) ||
            normalized.Contains("token", StringComparison.Ordinal) ||
            normalized.Contains("secret", StringComparison.Ordinal) ||
            normalized.Contains("authorization", StringComparison.Ordinal) ||
            normalized.Contains("credential", StringComparison.Ordinal) ||
            normalized.Contains("apikey", StringComparison.Ordinal) ||
            normalized.Contains("email", StringComparison.Ordinal) ||
            normalized.Contains("username", StringComparison.Ordinal) ||
            normalized.Contains("phone", StringComparison.Ordinal) ||
            normalized.Contains("mobile", StringComparison.Ordinal) ||
            normalized.Contains("address", StringComparison.Ordinal) ||
            normalized.Contains("dateofbirth", StringComparison.Ordinal) ||
            normalized.Equals("dob", StringComparison.Ordinal) ||
            normalized.Contains("ssn", StringComparison.Ordinal);
    }

    private sealed class LimitedCaptureStream : Stream
    {
        private readonly Stream _inner;
        private readonly int _captureLimit;
        private readonly MemoryStream _captured = new();

        public LimitedCaptureStream(Stream inner, int captureLimit)
        {
            _inner = inner;
            _captureLimit = captureLimit;
        }

        public bool IsTruncated { get; private set; }

        public byte[] CapturedBytes => _captured.ToArray();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            _inner.FlushAsync(cancellationToken);

        public override void Write(byte[] buffer, int offset, int count)
        {
            Capture(buffer.AsSpan(offset, count));
            _inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Capture(buffer);
            _inner.Write(buffer);
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            Capture(buffer.Span);
            return _inner.WriteAsync(buffer, cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _captured.Dispose();
            }

            base.Dispose(disposing);
        }

        private void Capture(ReadOnlySpan<byte> bytes)
        {
            var remaining = _captureLimit - (int)_captured.Length;
            if (remaining > 0)
            {
                _captured.Write(bytes[..Math.Min(bytes.Length, remaining)]);
            }

            if (bytes.Length > remaining)
            {
                IsTruncated = true;
            }
        }
    }
}
