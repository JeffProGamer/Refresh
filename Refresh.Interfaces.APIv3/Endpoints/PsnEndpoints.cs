using System.Net;
using System.Net.Http;
using System.Text;
using Bunkum.Core;
using Bunkum.Core.Endpoints;
using Bunkum.Core.Responses;
using Bunkum.Listener.Protocol;
using Bunkum.Protocols.Http;

namespace Refresh.Interfaces.APIv3.Endpoints;

public class PsnEndpoints : EndpointGroup
{
    private static readonly HttpClient HttpClient = new();

    // TODO: Add your ai-api.sh API key here if you want PSN username display
    // Get a key at https://ai-api.sh/ (optional — sticker works without it)
    private const string AiApiKey = "";

    [ApiV3Endpoint("psn-profile/{onlineId}")]
    [Authentication(false)]
    public Response GetPsnProfile(RequestContext context, string onlineId)
    {
        if (string.IsNullOrWhiteSpace(onlineId))
            return BadRequest;

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://rest.ai-api.sh/v1/psn/profile/{onlineId}");
            if (!string.IsNullOrWhiteSpace(AiApiKey))
                request.Headers.Add("X-API-Key", AiApiKey);

            HttpResponseMessage response = HttpClient.SendAsync(request).Result;

            if (!response.IsSuccessStatusCode)
                return NotFound;

            string body = response.Content.ReadAsStringAsync().Result;
            byte[] data = Encoding.UTF8.GetBytes(body);
            return new Response(data, ContentType.Json);
        }
        catch
        {
            return InternalServerError;
        }
    }
}
