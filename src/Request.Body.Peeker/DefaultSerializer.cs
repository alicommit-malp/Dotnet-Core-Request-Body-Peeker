using System;
using Newtonsoft.Json;

namespace Request.Body.Peeker
{
    public class DefaultSerializer : ISerializer
    {
        public T DeserializeObject<T>(string value)
        {
            return JsonConvert.DeserializeObject<T>(value) ?? throw new InvalidOperationException();
        }

        public string SerializeObject(object value)
        {
            return JsonConvert.SerializeObject(value);
        }
    }
}