using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace G30ServiceTool.Core;

/// <summary>Offline review text only. No executable TST export or diagnostic transport.</summary>
public static partial class Tool32Draft
{
    [GeneratedRegex(@"\A[0-9]{1,8}\z", RegexOptions.CultureInvariant)]
    private static partial Regex WorkshopPattern();

    public static string Build(ServiceEntry entry, string workshopCode)
    {
        EntryValidation.Validate(entry);
        if (!WorkshopPattern().IsMatch(workshopCode))
            throw new ValidationException("Anna vahvistettava korjaamotunnus (1–8 numeroa). Ohjelma ei keksi tunnusta puolestasi.");

        var fields = new List<string>
        {
            entry.Date.Day.ToString(CultureInfo.InvariantCulture),
            entry.Date.Month.ToString(CultureInfo.InvariantCulture),
            entry.Date.Year.ToString(CultureInfo.InvariantCulture), "0",
            entry.Mileage.ToString(CultureInfo.InvariantCulture),
            "0", // DIY, never claim a BMW dealer service.
            workshopCode, $"0x{(int)entry.Timing}", entry.Items.Length.ToString(CultureInfo.InvariantCulture)
        };
        foreach (var kind in entry.Items)
            fields.AddRange([((int)kind).ToString(CultureInfo.InvariantCulture), $"0x{(int)entry.Timing}", "0", "0"]);

        return $"""
            G30 SERVICE TOOL / TOOL32-LUONNOS
            TARKASTELUTEKSTI – EI AJETTAVA TST-TIEDOSTO

            NBT EVO -yhteensopivuutta ei ole vahvistettu.
            Alla oleva rivi perustuu NBT:llä testatun lähdeprojektin g_mmi-muotoon.
            g_mmi-ryhmän sopivuutta, nbtevo.prg-tiedostoa, argumenttien merkityksiä
            ja tilakoodeja ei ole validoitu tämän auton ohjelmistoversiolle.
            Älä muuta tiedostopäätettä ja aja riviä autoon tämän luonnoksen perusteella.

            Merkintä: {entry.Id}
            Auto: BMW G30 · 2018 (autosta ei luettu tunnistetietoja)
            Huoltopäivä: {entry.DateLabel}
            Kilometrit: {entry.MileageLabel}
            Huollot: {entry.ItemsLabel}
            Ajankohtaisuus: {entry.TimingLabel} (sama tila kaikille valituille töille)
            Tekijä: itse tehty huolto; BMW-lippu = 0
            Korjaamotunnus: {workshopCode} (käyttäjän antama, tarkistettava)

            EHDOTETTU NBT-VIITERIVI:
            1|g_mmi|steuern_servicehistory_add||{string.Join(';', fields)}

            Muistiinpanot säilyvät paikallisessa huoltokirjassa. Niitä ei koodata viiteriviin.
            Tämä luonnos ei lue, muuta tai varmuuskopioi auton huoltohistoriaa.
            Ei BMW-verkkotietokannan päivitystä eikä CBS-nollausta.

            Ennen tulevaa kirjoitustukea tarvitaan yksikön ja SGBD/job-kuvauksen tunnistus,
            nykyisen autohistorian talteenotto, testattu lisäävä kirjoitus, vakaa
            syöttöjännite ja autosta takaisin luetun tuloksen vertailu.

            Tekninen viite: https://github.com/theDeniZ/ServiceManager
            """;
    }

    public static void Save(string destination, string draft)
    {
        if (!string.Equals(Path.GetExtension(destination), ".txt", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Tool32-luonnoksen tiedostopäätteen pitää olla .txt.");
        File.WriteAllText(destination, draft, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }
}
