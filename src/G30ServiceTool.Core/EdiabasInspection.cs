using System.Reflection.PortableExecutable;
using System.Text;

namespace G30ServiceTool.Core;

public sealed record InstallationFile(string Name, string Path, bool Exists);

public sealed record EdiabasReport(
    string Tool32Path, string Tool32Architecture, string IniPath, string Interface,
    string EcuDirectory, InstallationFile[] Files, string[] Notes)
{
    public string ToText()
    {
        var text = new StringBuilder();
        text.AppendLine("TOOL32 / EDIABAS – PAIKALLINEN TARKISTUS");
        text.AppendLine($"Tool32: {Tool32Path}");
        text.AppendLine($"EXE-rakenne: {Tool32Architecture}");
        text.AppendLine($"EDIABAS.INI: {IniPath}");
        text.AppendLine($"Interface (Configuration): {Interface}");
        text.AppendLine($"ECU-kansio: {EcuDirectory}");
        text.AppendLine();
        foreach (var file in Files)
            text.AppendLine($"{(file.Exists ? "LÖYTYI" : "PUUTTUU")} · {file.Name} · {file.Path}");
        text.AppendLine();
        foreach (var note in Notes) text.AppendLine("• " + note);
        text.AppendLine();
        text.AppendLine("Tiedostojen löytyminen ei vahvista toimivaa EDIABAS-asennusta, auton yksikköä tai job-tukea.");
        text.AppendLine("DLL-tiedostoja ei ladattu, Tool32:ta ei käynnistetty eikä autoon otettu yhteyttä.");
        return text.ToString();
    }
}

/// <summary>Inspects selected local files. Never loads native DLLs or runs diagnostic jobs.</summary>
public static class EdiabasInspection
{
    public static EdiabasReport Inspect(string tool32Path, string? ecuDirectoryOverride = null)
    {
        var tool = LocalPath(tool32Path);
        if (!string.Equals(Path.GetFileName(tool), "tool32.exe", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Valitse Tool32.exe-tiedosto.");
        if (!File.Exists(tool)) throw new ValidationException("Valittua Tool32.exe-tiedostoa ei löydy.");
        var bin = Path.GetDirectoryName(tool)!;
        var root = Path.GetDirectoryName(bin) ?? bin;
        var notes = new List<string>();
        var ini = Path.Combine(bin, "EDIABAS.INI");
        var config = File.Exists(ini) ? ReadConfiguration(ini) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(ini)) notes.Add("EDIABAS.INI puuttuu Tool32:n kansiosta. Muualla olevia asetuksia ei etsitty.");
        var interfaceName = config.GetValueOrDefault("Interface", "Ei määritetty");
        string ecu;
        if (!string.IsNullOrWhiteSpace(ecuDirectoryOverride))
        {
            ecu = LocalPath(ecuDirectoryOverride);
            notes.Add("ECU-kansio valittiin käsin. Tämä valinta ei muuta EDIABASin asetuksia.");
        }
        else if (config.TryGetValue("EcuPath", out var configured) && !string.IsNullOrWhiteSpace(configured))
        {
            try { ecu = LocalPath(Environment.ExpandEnvironmentVariables(configured.Trim('"'))); }
            catch (ValidationException)
            {
                ecu = "";
                notes.Add("INI:n EcuPath ei ole yksiselitteinen paikallinen absoluuttinen polku. Valitse ECU-kansio käsin.");
            }
        }
        else
        {
            ecu = Path.Combine(root, "ECU");
            notes.Add("EcuPath puuttuu: tarkistettiin Tool32-kansion viereinen ECU-kansio. Tämä on kansioehdokas.");
        }
        if (ecu.Length > 0 && !Directory.Exists(ecu)) notes.Add("ECU-kansiota ei löydy. Tarkista valinta tällä läppärillä.");
        var files = new List<InstallationFile>
        {
            new("Tool32.exe", tool, true),
            Check("EDIABAS.INI", ini),
            Check("api32.dll", Path.Combine(bin, "api32.dll"))
        };
        if (ecu.Length > 0)
            foreach (var name in new[] { "nbtevo.prg", "nbt.prg", "g_mmi.grp" })
                files.Add(Check(name, Path.Combine(ecu, name)));
        else notes.Add("PRG/GRP-tiedostoja ei tarkistettu ilman vahvistettua kansiopolkua.");
        var architecture = Architecture(tool);
        if (architecture == "x86 (32-bittinen)")
            notes.Add("Tool32 on 32-bittinen. Mahdollisen natiivin EDIABAS-rajapinnan bittisyys tarkistetaan erikseen ennen integraatiota.");
        return new(tool, architecture, ini, interfaceName, ecu, files.ToArray(), notes.ToArray());
    }

    private static InstallationFile Check(string name, string path) => new(name, path, File.Exists(path));

    private static string LocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ValidationException("Valitse paikallinen tiedosto tai kansio.");
        var trimmed = path.Trim().Trim('"');
        // Explicit drive paths only. No UNC access, device paths or drive-relative resolution.
        if (trimmed.Length < 3 || !char.IsAsciiLetter(trimmed[0]) || trimmed[1] != ':' ||
            (trimmed[2] != '\\' && trimmed[2] != '/') || trimmed.Contains('%') || trimmed[2..].Contains(':'))
            throw new ValidationException("Anna paikallinen absoluuttinen polku, esimerkiksi C:\\EDIABAS\\BIN\\Tool32.exe.");
        try { return Path.GetFullPath(trimmed); }
        catch (ArgumentException) { throw new ValidationException("Tiedosto- tai kansiopolku on virheellinen."); }
    }

    private static Dictionary<string, string> ReadConfiguration(string path)
    {
        if (new FileInfo(path).Length > 1024 * 1024)
            throw new ValidationException("EDIABAS.INI on poikkeuksellisen suuri (yli 1 Mt). Tarkistusta ei tehty.");
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var section = "";
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#') continue;
            if (line.StartsWith('['))
            {
                var end = line.IndexOf(']');
                section = end > 1 ? line[1..end].Trim() : "";
                continue;
            }
            if (!section.Equals("Configuration", StringComparison.OrdinalIgnoreCase)) continue;
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            var key = line[..separator].Trim();
            if (!key.Equals("Interface", StringComparison.OrdinalIgnoreCase) && !key.Equals("EcuPath", StringComparison.OrdinalIgnoreCase)) continue;
            if (!result.TryAdd(key, line[(separator + 1)..].Split(';', 2)[0].Trim()))
                throw new ValidationException($"EDIABAS.INI sisältää toistuvan {key}-asetuksen. Tarkista tiedosto Tool32-läppärillä.");
        }
        return result;
    }

    private static string Architecture(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            return pe.PEHeaders.CoffHeader.Machine switch
            {
                Machine.I386 => "x86 (32-bittinen)",
                Machine.Amd64 => "x64 (64-bittinen)",
                Machine.Arm64 => "ARM64",
                _ => "Tuntematon PE-rakenne"
            };
        }
        catch (BadImageFormatException) { return "Ei tunnistettava Windows PE -tiedosto"; }
    }
}
