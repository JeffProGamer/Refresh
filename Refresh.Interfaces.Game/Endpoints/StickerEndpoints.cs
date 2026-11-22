using System.IO;
using Bunkum.Core;
using Bunkum.Core.Endpoints;
using Bunkum.Core.Responses;
using Bunkum.Listener.Protocol;
using Bunkum.Protocols.Http;

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

    // Unauthenticated hash route so the PS4 can fetch lbpwn.ff by GUID without login
    [GameEndpoint("r/c8a8d8eddcb9819ce1a318bdc4854900ce6a8280")]
    [Authentication(false)]
    public Response GetStickerScriptByHash(RequestContext context)
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
}
