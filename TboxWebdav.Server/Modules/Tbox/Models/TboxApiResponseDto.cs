using Newtonsoft.Json;
using System.Net;

namespace TboxWebdav.Server.Modules.Tbox.Models
{
    public abstract class TboxApiResponseDto
    {
        [JsonIgnore]
        public HttpStatusCode? HttpStatusCode { get; internal set; }

        [JsonIgnore]
        public TboxErrorMessageDto? Error { get; internal set; }

        [JsonIgnore]
        public string? ResponseContent { get; internal set; }
    }
}
