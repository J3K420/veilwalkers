using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace Veilwalkers.Persistence
{
    /// <summary>
    /// The single Newtonsoft configuration for save serialization: camelCase property
    /// names, dictionary keys left VERBATIM (monster ids like <c>"mon01"</c> must
    /// never be re-cased by the resolver — hence a camelCase naming strategy with
    /// <c>processDictionaryKeys: false</c> rather than the stock
    /// <c>CamelCasePropertyNamesContractResolver</c>, which re-cases keys), and
    /// dedicated converters for the UnityEngine math types inside
    /// <c>AnchorToken</c> — plain Json.NET cannot serialize <see cref="Vector3"/> /
    /// <see cref="Quaternion"/> because their <c>normalized</c> properties
    /// self-reference. Internal: serialization details never leak past
    /// <see cref="LocalProgressStore"/>.
    /// </summary>
    internal static class SaveJson
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy(
                    processDictionaryKeys: false, overrideSpecifiedNames: true),
            },

            // Replace (not the default Auto/append) so deserializing into the POCOs'
            // pre-initialized empty collections can never double-populate.
            ObjectCreationHandling = ObjectCreationHandling.Replace,

            // Dates are ISO-8601 UTC STRINGS by architecture rule. Json.NET's default
            // DateParseHandling would silently convert ISO-looking strings to DateTime
            // and re-format them on read — None keeps them verbatim.
            DateParseHandling = DateParseHandling.None,
            Converters = { new Vector3Converter(), new QuaternionConverter() },
            Formatting = Formatting.None,
        };

        public static JsonSerializer CreateSerializer() => JsonSerializer.Create(Settings);

        private sealed class Vector3Converter : JsonConverter<Vector3>
        {
            public override void WriteJson(JsonWriter writer, Vector3 value, JsonSerializer serializer)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("x");
                writer.WriteValue(value.x);
                writer.WritePropertyName("y");
                writer.WriteValue(value.y);
                writer.WritePropertyName("z");
                writer.WriteValue(value.z);
                writer.WriteEndObject();
            }

            public override Vector3 ReadJson(
                JsonReader reader, Type objectType, Vector3 existingValue,
                bool hasExistingValue, JsonSerializer serializer)
            {
                JObject obj = JObject.Load(reader);
                return new Vector3(
                    obj.Value<float?>("x") ?? 0f,
                    obj.Value<float?>("y") ?? 0f,
                    obj.Value<float?>("z") ?? 0f);
            }
        }

        private sealed class QuaternionConverter : JsonConverter<Quaternion>
        {
            public override void WriteJson(JsonWriter writer, Quaternion value, JsonSerializer serializer)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("x");
                writer.WriteValue(value.x);
                writer.WritePropertyName("y");
                writer.WriteValue(value.y);
                writer.WritePropertyName("z");
                writer.WriteValue(value.z);
                writer.WritePropertyName("w");
                writer.WriteValue(value.w);
                writer.WriteEndObject();
            }

            public override Quaternion ReadJson(
                JsonReader reader, Type objectType, Quaternion existingValue,
                bool hasExistingValue, JsonSerializer serializer)
            {
                JObject obj = JObject.Load(reader);
                return new Quaternion(
                    obj.Value<float?>("x") ?? 0f,
                    obj.Value<float?>("y") ?? 0f,
                    obj.Value<float?>("z") ?? 0f,
                    obj.Value<float?>("w") ?? 1f);
            }
        }
    }
}
