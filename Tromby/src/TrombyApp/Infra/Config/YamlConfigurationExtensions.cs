using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Configuration;
using YamlDotNet.Serialization;

namespace Tromby.Infra.Config;

/// <summary>
/// Provides YAML configuration loading via YamlDotNet.
/// </summary>
public static class YamlConfigurationExtensions
{
    /// <summary>
    /// Adds a YAML configuration file to the builder.
    /// </summary>
    public static IConfigurationBuilder AddYamlFile(this IConfigurationBuilder builder, string path, bool optional)
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, path);
        if (!File.Exists(fullPath))
        {
            if (!optional)
            {
                throw new FileNotFoundException($"Configuration file '{path}' not found.");
            }

            return builder;
        }

        var deserializer = new DeserializerBuilder().Build();
        var serializer = new SerializerBuilder().JsonCompatible().Build();
        using var reader = File.OpenText(fullPath);
        var yamlObject = deserializer.Deserialize(reader);
        var json = serializer.Serialize(yamlObject);
        builder.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)));
        return builder;
    }
}
