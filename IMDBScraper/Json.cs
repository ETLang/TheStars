using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace IMDBScraper
{
    public static class Json
    {
        private static JsonSerializerOptions _GlobalOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters =
            {
                new JsonStringEnumConverter() 
            }
        };

        public static void Serialize<TValue>(Stream utf8Json, TValue value)
        {
            JsonSerializer.Serialize(utf8Json, value, _GlobalOptions);
        }

        public static string Serialize<TValue>(TValue value)
        {
            return JsonSerializer.Serialize(value, _GlobalOptions);
        }

        public static TValue? Deserialize<TValue>(Stream utf8Json)
        {
            return JsonSerializer.Deserialize<TValue>(utf8Json, _GlobalOptions);
        }

        public static TValue? Deserialize<TValue>(string json)
        {
            return JsonSerializer.Deserialize<TValue>(json, _GlobalOptions);
        }
    }
}
