using System;
using System.IO;
using System.Text;
using Bunkum.Core;
using Bunkum.Core.Endpoints;
using Bunkum.Core.Responses;
using Bunkum.Listener.Protocol;
using Bunkum.Protocols.Http;
using Refresh.Core.RateLimits.EndpointRateLimiting;

namespace Refresh.Interfaces.Game.Endpoints;

public class StickerEndpoints : EndpointGroup
{
    private static readonly string StickerScriptPath = Path.Combine(AppContext.BaseDirectory, "lbpwn.ff");
    private static readonly string StickerPlanPath = Path.Combine(AppContext.BaseDirectory, "lbp_reborn_sticker.plan");

    [GameEndpoint("sticker-script")]
    [Authentication(false)]
    public Response GetStickerScript(RequestContext context)
    {
        if (!File.Exists(StickerScriptPath))
            return NotFound;

        byte[] data = File.ReadAllBytes(StickerScriptPath);
        return new Response(data, ContentType.BinaryData);
    }

    [GameEndpoint("sticker-plan")]
    [Authentication(false)]
    public Response GetStickerPlan(RequestContext context)
    {
        if (!File.Exists(StickerPlanPath))
            return NotFound;

        byte[] data = File.ReadAllBytes(StickerPlanPath);
        return new Response(data, ContentType.BinaryData);
    }

    // Debug endpoint: dumps everything the PS4 sends so you can debug handshake issues
    [GameEndpoint("debug-dump")]
    [Authentication(false)]
    [EndpointRateLimit(EndpointBucketId.GameDownloadAsset)]
    public Response DebugDump(RequestContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== REQUEST DUMP ===");
        sb.AppendLine($"Method: {context.Method}");
        sb.AppendLine($"URL: {context.Url}");
        sb.AppendLine($"Remote: {context.RemoteEndpoint}");
        sb.AppendLine();

        sb.AppendLine("--- HEADERS ---");
        foreach (string? key in context.RequestHeaders.AllKeys)
            sb.AppendLine($"{key}: {context.RequestHeaders[key]}");
        sb.AppendLine();

        sb.AppendLine("--- COOKIES ---");
        foreach (string? key in context.Cookies.AllKeys)
            sb.AppendLine($"{key}={context.Cookies[key]}");
        sb.AppendLine();

        sb.AppendLine("--- QUERY STRING ---");
        foreach (string? key in context.QueryString.AllKeys)
            sb.AppendLine($"{key}={context.QueryString[key]}");
        sb.AppendLine();

        // Read the body
        byte[]? body = null;
        try
        {
            using var ms = new MemoryStream();
            context.RequestStream.CopyTo(ms);
            body = ms.ToArray();
        }
        catch { }

        sb.AppendLine($"--- BODY ({body?.Length ?? 0} bytes) ---");
        if (body != null && body.Length > 0)
        {
            sb.AppendLine($"Hex: {BitConverter.ToString(body).Replace("-", " ")}");
            sb.AppendLine($"UTF8: {Encoding.UTF8.GetString(body)}");
        }
        sb.AppendLine();

        // Log it server-side too
        context.Logger.LogWarning(BunkumCategory.Authentication, $"PS4 DEBUG DUMP:\n{sb}");

        return new Response(Encoding.UTF8.GetBytes(sb.ToString()), ContentType.Plaintext);
    }

    // Sticker ping endpoint: PS4 sticker script calls this to verify connection works
    [GameEndpoint("sticker-ping")]
    [Authentication(false)]
    [EndpointRateLimit(EndpointBucketId.GameDownloadAsset)]
    public Response StickerPing(RequestContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== STICKER PING ===");
        sb.AppendLine($"Method: {context.Method}");
        sb.AppendLine($"URL: {context.Url}");
        sb.AppendLine($"Remote: {context.RemoteEndpoint}");
        sb.AppendLine($"Time: {DateTimeOffset.UtcNow:O}");
        sb.AppendLine();

        sb.AppendLine("--- HEADERS ---");
        foreach (string? key in context.RequestHeaders.AllKeys)
            sb.AppendLine($"{key}: {context.RequestHeaders[key]}");
        sb.AppendLine();

        sb.AppendLine("--- COOKIES ---");
        foreach (string? key in context.Cookies.AllKeys)
            sb.AppendLine($"{key}={context.Cookies[key]}");
        sb.AppendLine();

        // Read the body
        byte[]? body = null;
        try
        {
            using var ms = new MemoryStream();
            context.RequestStream.CopyTo(ms);
            body = ms.ToArray();
        }
        catch { }

        sb.AppendLine($"--- BODY ({body?.Length ?? 0} bytes) ---");
        if (body != null && body.Length > 0)
        {
            sb.AppendLine($"Hex: {BitConverter.ToString(body).Replace("-", " ")}");
            sb.AppendLine($"UTF8: {Encoding.UTF8.GetString(body)}");
        }
        sb.AppendLine();

        // Log it server-side
        context.Logger.LogWarning(BunkumCategory.Authentication, $"STICKER PING:\n{sb}");

        return new Response(Encoding.UTF8.GetBytes(sb.ToString()), ContentType.Plaintext);
    }
}
