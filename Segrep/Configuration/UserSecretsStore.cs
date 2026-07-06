using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration.UserSecrets;

namespace Segrep.Configuration;

public static class UserSecretsStore
{
    public static string GetSecretsFilePath(Assembly assembly)
    {
        var id = assembly.GetCustomAttribute<UserSecretsIdAttribute>()?.UserSecretsId
            ?? throw new InvalidOperationException($"Assembly '{assembly.GetName().Name}' does not define a {nameof(UserSecretsIdAttribute)}.");

        var profileRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(profileRoot, ".microsoft", "usersecrets", id, "secrets.json");
    }

    public static JsonObject Load(string path)
    {
        if (!File.Exists(path))
        {
            return new JsonObject();
        }

        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new JsonObject();
        }

        return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
    }

    public static void Save(string path, JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void SetSection(JsonObject root, string section, IDictionary<string, string?> values)
    {
        if (root[section] is not JsonObject sectionNode)
        {
            sectionNode = new JsonObject();
            root[section] = sectionNode;
        }

        foreach (var (key, value) in values)
        {
            sectionNode[key] = value;
        }
    }
}
