#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TabBouncer;

// 화면 문구는 languages.json 한 파일에 언어별로 나눠 둔다. 실행 파일에 넣은 사본을 먼저 읽고,
// 실행 파일 옆에 languages.json이 있으면 그 내용을 덮어써서 번역을 고치거나 언어를 더할 수 있게 한다.
// 현재 언어에 없는 문구는 기본 언어(defaultLanguage) 문구로, 그것도 없으면 키를 그대로 보여 준다.
internal static class L
{
    internal const string FileName = "languages.json";
    private const string ResourceName = "TabBouncer.languages.json";

    internal sealed record LanguageInfo(string Code, string Name);

    private static readonly List<LanguageInfo> LanguageList = new();
    private static readonly Dictionary<string, Dictionary<string, string>> Tables =
        new(StringComparer.OrdinalIgnoreCase);
    private static string _defaultLanguage = "ko";

    // 파일에 적힌 순서다. 설정 창의 언어 목록도 이 순서를 따른다.
    internal static IReadOnlyList<LanguageInfo> Languages => LanguageList;
    internal static string DefaultLanguage => _defaultLanguage;
    internal static string Language { get; private set; } = "";

    // 실행 파일 옆 languages.json을 읽지 못한 이유다. 로그를 쓸 수 있게 된 뒤 시작 로그에 남긴다.
    internal static string LoadError { get; private set; } = "";

    static L()
    {
        using (Stream? embedded = typeof(L).Assembly.GetManifestResourceStream(ResourceName))
        {
            if (embedded is not null)
                Merge(embedded);
        }

        try
        {
            string external = Path.Combine(AppContext.BaseDirectory, FileName);
            if (File.Exists(external))
            {
                using FileStream stream = File.OpenRead(external);
                Merge(stream);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LoadError = ex.Message;
        }

        if (FindCode(_defaultLanguage) is { } known)
            _defaultLanguage = known;
        else if (LanguageList.Count > 0)
            _defaultLanguage = LanguageList[0].Code;
        Language = Detect("");
    }

    // configured가 비어 있거나 표에 없으면 Windows 표시 언어를, 그것도 표에 없으면 기본 언어를 쓴다.
    internal static void SetLanguage(string? configured) => Language = Detect(configured);

    // 표에 있는 언어 코드면 표에 적힌 표기로, 아니면 ""(Windows 설정 따르기)로 바꾼다.
    internal static string NormalizeLanguage(string? code) => FindCode(code) ?? "";

    internal static bool Has(string key) => Lookup(Language, key) is not null || Lookup(_defaultLanguage, key) is not null;

    internal static string T(string key) => Lookup(Language, key) ?? Lookup(_defaultLanguage, key) ?? key;

    internal static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(key), args);

    // 기본 언어와 비교해 빠진 키, 남는 키, {0} 같은 자리표시자가 다른 문구를 찾는다. 자체 테스트에서 쓴다.
    internal static IEnumerable<string> FindTableProblems()
    {
        if (!Tables.TryGetValue(_defaultLanguage, out Dictionary<string, string>? baseline))
        {
            yield return "default language not found: " + _defaultLanguage;
            yield break;
        }

        foreach (LanguageInfo language in LanguageList.Where(language => language.Code != _defaultLanguage))
        {
            Dictionary<string, string> table = Tables[language.Code];
            foreach (string key in baseline.Keys.Where(key => !table.ContainsKey(key)))
                yield return $"{language.Code}: missing {key}";
            foreach (string key in table.Keys.Where(key => !baseline.ContainsKey(key)))
                yield return $"{language.Code}: unknown {key}";
            foreach ((string key, string value) in table.Where(entry => baseline.ContainsKey(entry.Key)))
            {
                if (!Placeholders(value).SequenceEqual(Placeholders(baseline[key])))
                    yield return $"{language.Code}: placeholders differ in {key}";
            }
        }
    }

    private static IEnumerable<string> Placeholders(string text) =>
        Regex.Matches(text, @"\{(\d+)[^}]*\}").Select(match => match.Groups[1].Value).Distinct().Order();

    private static string? Lookup(string language, string key) =>
        Tables.TryGetValue(language, out Dictionary<string, string>? table) &&
        table.TryGetValue(key, out string? value)
            ? value
            : null;

    private static string? FindCode(string? code)
    {
        string text = (code ?? "").Trim();
        return text.Length == 0
            ? null
            : LanguageList.FirstOrDefault(language => language.Code.Equals(text, StringComparison.OrdinalIgnoreCase))?.Code;
    }

    private static string Detect(string? configured)
    {
        if (FindCode(configured) is { } chosen)
            return chosen;
        for (CultureInfo culture = CultureInfo.CurrentUICulture; culture.Name.Length > 0; culture = culture.Parent)
        {
            if (FindCode(culture.Name) is { } matched)
                return matched;
        }
        return _defaultLanguage;
    }

    // 형식: { "defaultLanguage": "ko", "languages": { "ko": { "name": "한국어", "strings": { "키": "문구" } } } }
    // 이미 있는 언어면 이름과 문구를 덮어쓰고, 없는 언어면 목록 끝에 더한다.
    private static void Merge(Stream stream)
    {
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("The root of " + FileName + " must be an object.");

        if (root.TryGetProperty("languages", out JsonElement languages) && languages.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty language in languages.EnumerateObject())
            {
                string code = language.Name.Trim();
                if (code.Length == 0 || language.Value.ValueKind != JsonValueKind.Object)
                    continue;

                int index = LanguageList.FindIndex(item => item.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    LanguageList.Add(new LanguageInfo(code, code));
                    Tables[code] = new Dictionary<string, string>(StringComparer.Ordinal);
                    index = LanguageList.Count - 1;
                }

                if (language.Value.TryGetProperty("name", out JsonElement name) &&
                    name.ValueKind == JsonValueKind.String && name.GetString() is { Length: > 0 } displayName)
                    LanguageList[index] = LanguageList[index] with { Name = displayName };

                if (!language.Value.TryGetProperty("strings", out JsonElement strings) ||
                    strings.ValueKind != JsonValueKind.Object)
                    continue;
                Dictionary<string, string> table = Tables[LanguageList[index].Code];
                foreach (JsonProperty entry in strings.EnumerateObject())
                {
                    if (entry.Value.ValueKind == JsonValueKind.String)
                        table[entry.Name] = entry.Value.GetString()!;
                }
            }
        }

        if (root.TryGetProperty("defaultLanguage", out JsonElement fallback) &&
            fallback.ValueKind == JsonValueKind.String && FindCode(fallback.GetString()) is { } defaultCode)
            _defaultLanguage = defaultCode;
    }
}
