using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Dalamud.Game;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace StratBoardImport.Localization;

public static class Loc
{
    public const string Auto = "auto";

    public static readonly string[] SupportedCultures = ["en-UK", "de-DE", "fr-FR", "ja-JP"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Dictionary<string, string> Fallback = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Current = new(StringComparer.Ordinal);

    private static IDalamudPluginInterface? pluginInterface;
    private static Configuration? configuration;
    private static IClientState? clientState;
    private static IPluginLog? log;

    public static string ResolvedCulture { get; private set; } = "en-UK";

    public static void Initialize(
        IDalamudPluginInterface pluginInterface,
        Configuration configuration,
        IClientState clientState,
        IPluginLog log)
    {
        Loc.pluginInterface = pluginInterface;
        Loc.configuration = configuration;
        Loc.clientState = clientState;
        Loc.log = log;
        Reload();
    }

    public static void Reload()
    {
        LoadInto(Fallback, "en-UK");
        ResolvedCulture = ResolveCulture();
        Current.Clear();
        if (!ResolvedCulture.Equals("en-UK", StringComparison.OrdinalIgnoreCase))
            LoadInto(Current, ResolvedCulture);
    }

    public static string Get(string key)
    {
        if (Current.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
            return value;
        if (Fallback.TryGetValue(key, out value) && !string.IsNullOrEmpty(value))
            return value;
        return key;
    }

    public static string Format(string key, params object?[] args)
    {
        var template = Get(key);
        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    public static string CultureLabel(string culture)
    {
        if (culture.Equals(Auto, StringComparison.OrdinalIgnoreCase))
            return Format(L.UiLanguageAuto, CultureLabel(ResolveCultureFromGame()));

        return culture.ToUpperInvariant() switch
        {
            "EN-UK" => "English (UK)",
            "DE-DE" => "Deutsch",
            "FR-FR" => "Français",
            "JA-JP" => "日本語",
            _ => culture,
        };
    }

    private static string ResolveCulture()
    {
        var selected = configuration?.Language ?? Auto;
        if (!string.IsNullOrWhiteSpace(selected) &&
            !selected.Equals(Auto, StringComparison.OrdinalIgnoreCase) &&
            SupportedCultures.Contains(selected, StringComparer.OrdinalIgnoreCase))
        {
            return SupportedCultures.First(c => c.Equals(selected, StringComparison.OrdinalIgnoreCase));
        }

        return ResolveCultureFromGame();
    }

    private static string ResolveCultureFromGame()
    {
        return clientState?.ClientLanguage switch
        {
            ClientLanguage.German => "de-DE",
            ClientLanguage.French => "fr-FR",
            ClientLanguage.Japanese => "ja-JP",
            _ => "en-UK",
        };
    }

    private static void LoadInto(Dictionary<string, string> target, string culture)
    {
        target.Clear();
        var json = ReadOverrideFile(culture) ?? ReadEmbedded(culture);
        if (json == null)
        {
            log?.Warning($"[SBI] Translation file for {culture} was not found.");
            return;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
            if (parsed == null)
                return;

            foreach (var (key, value) in parsed)
            {
                if (!string.IsNullOrWhiteSpace(key) && value != null)
                    target[key] = value;
            }
        }
        catch (Exception ex)
        {
            log?.Error(ex, $"[SBI] Failed to parse translation file for {culture}.");
        }
    }

    private static string? ReadOverrideFile(string culture)
    {
        var directory = pluginInterface?.AssemblyLocation.DirectoryName;
        if (string.IsNullOrEmpty(directory))
            return null;

        var paths = new[]
        {
            Path.Combine(directory, "Translations", $"{culture}.json"),
            Path.Combine(directory, "Localization", "Translations", $"{culture}.json"),
        };

        foreach (var path in paths)
        {
            if (!File.Exists(path))
                continue;

            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                log?.Warning(ex, $"[SBI] Could not read {path}.");
            }
        }

        return null;
    }

    private static string? ReadEmbedded(string culture)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = $"StratBoardImport.Localization.Translations.{culture}.json";
        using var stream = assembly.GetManifestResourceStream(name);
        if (stream == null)
            return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
