using System.Text.Json;
using System.Text.Json.Serialization;

public static class JsonHelper
{
    public static bool Deserialize<T>(string json, out T? result)
    {
        try
        {
            result = JsonSerializer.Deserialize<T>(json, options: new JsonSerializerOptions { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true });
            return result != null;
        }
        catch (Exception)
        {
            result = default;
            return false;
        }
    }
    
    public static string Serialize<T>(T obj)
    {
        return JsonSerializer.Serialize(obj);
    }

    public record MidPointResponse(
        [property: JsonPropertyName("object")] MidPointObjectContainer Object
    );

    public record MidPointObjectContainer(
        [property: JsonPropertyName("@type")] string Type,
        [property: JsonPropertyName("object")] List<MidPointUser> UsersList
    );

    public record MidPointUser(
        [property: JsonPropertyName("@type")] string Type,
        [property: JsonPropertyName("oid")] string Oid,
        [property: JsonPropertyName("name")] string Name
    );

    public record MidpointPatchResponse(
        [property: JsonPropertyName("object")] OperationResult Response
    );

    public record OperationResult(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("message")] string Message
    );
}
