global using L = sts2MultiplayerSpectatorModule.Scripts.ModLocalization;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 按游戏语言读取内嵌 JSON，缺少键或语言时回退到英文。
// Load embedded JSON for the game language, falling back to English for missing translations.
internal static class ModLocalization
{
    internal static Func<string> Language = () => "eng";
    private static readonly LocalizationCatalog Catalog = new(Path.Combine(
        Path.GetDirectoryName(typeof(ModLocalization).Assembly.Location)!, "localization"), embedded: true);
    internal static string T(string key, params object?[] args) => Catalog.Text(Language(), key, args);

    // 网络只携带键和参数，让接收端用自己的语言显示状态和事件提示。
    // Send keys and arguments so recipients render statuses and event hints in their own language.
    private sealed record WireText(string Key, string[] Args);
    internal static string Wire(string key, params object?[] args) => "[[spectator:" + Convert.ToBase64String(
        JsonSerializer.SerializeToUtf8Bytes(new WireText(key, args.Select(value => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "").ToArray()))) + "]]";
    internal static string Resolve(string text) => Regex.Replace(text, @"\[\[spectator:([A-Za-z0-9+/=]{1,8192})\]\]", match =>
    {
        try
        {
            var value = JsonSerializer.Deserialize<WireText>(Convert.FromBase64String(match.Groups[1].Value));
            return value is { Key.Length: <= 100, Args.Length: <= 8 } ? T(value.Key, value.Args.Cast<object>().ToArray()) : match.Value;
        }
        catch (Exception error) when (error is JsonException or FormatException) { return match.Value; }
    });
}

// 缓存各语言表；目录名使用游戏的 eng、zhs、zht 等语言代码。
// Cache language tables using game codes such as eng, zhs and zht.
internal sealed class LocalizationCatalog(string directory, bool embedded = false)
{
    private readonly Dictionary<string, Dictionary<string, string>> _tables = new();
    private Dictionary<string, string> Table(string language)
    {
        if (language.Length is < 2 or > 8 || !language.All(char.IsAsciiLetterOrDigit)) language = "eng";
        if (_tables.TryGetValue(language, out var table)) return table;
        try
        {
            using var stream = embedded ? ModResources.Open("localization." + language + ".spectator.json")
                : File.OpenRead(Path.Combine(directory, language, "spectator.json"));
            table = stream == null ? new() : JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { table = new(); }
        return _tables[language] = table;
    }
    internal string Text(string language, string key, params object?[] args)
    {
        string fallback = Table("eng").GetValueOrDefault(key) ?? "[" + key + "]";
        string text = Table(language).GetValueOrDefault(key) ?? fallback;
        try { return string.Format(CultureInfo.CurrentCulture, text, args); }
        catch (FormatException)
        {
            try { return string.Format(CultureInfo.CurrentCulture, fallback, args); }
            catch (FormatException) { return "[" + key + "]"; }
        }
    }
}

// 资源名称按路径后缀读取，源码目录结构保持不变。
// Find embedded resources by path suffix without changing the source asset layout.
internal static class ModResources
{
    internal static Stream? Open(string suffix)
    {
        var assembly = typeof(ModResources).Assembly;
        string? name = assembly.GetManifestResourceNames().FirstOrDefault(name =>
            name.Replace('\\', '.').Replace('/', '.').EndsWith("." + suffix, StringComparison.Ordinal));
        return name == null ? null : assembly.GetManifestResourceStream(name);
    }
}
