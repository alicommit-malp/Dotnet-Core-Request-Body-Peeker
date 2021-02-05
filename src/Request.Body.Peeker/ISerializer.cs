namespace Request.Body.Peeker
{
    public interface ISerializer
    {
        T DeserializeObject<T>(string value);
        string SerializeObject(object value);
    }
}