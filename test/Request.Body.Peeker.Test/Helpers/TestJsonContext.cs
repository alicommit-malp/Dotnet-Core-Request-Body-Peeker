using System.Text.Json.Serialization;

namespace Request.Body.Peeker.Test.Helpers
{
    public sealed record Person(string Name, string SurName);

    public readonly record struct Point(int X, int Y);

    [JsonSerializable(typeof(Person))]
    public partial class TestJsonContext : JsonSerializerContext
    {
    }
}
