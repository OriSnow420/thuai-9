namespace Thuai.Protocol.Messages;

using System.Text.Json;
using System.Text.Json.Serialization;

public record Message
{
    [JsonPropertyName("messageType")]
    public virtual string MessageType { get; init; } = "";

    [JsonIgnore]
    public string Json => JsonSerializer.Serialize((object)this, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public abstract record PerformMessage : Message
{
    [JsonPropertyName("token")]
    public string Token { get; init; } = "";
}
