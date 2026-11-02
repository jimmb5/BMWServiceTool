using System.Globalization;
using System.Text.Json.Serialization;

namespace G30ServiceTool.Core;

// These numeric IDs describe the NBT reference project, not verified NBT EVO jobs.
[JsonConverter(typeof(JsonStringEnumConverter<ServiceKind>))]
public enum ServiceKind
{
    EngineOil = 1, FrontBrakes = 2, BrakeFluid = 3, CabinFilter = 4,
    RearBrakes = 6, SparkPlugs = 10, AirFilter = 11, FuelFilter = 12, VehicleCheck = 100
}

[JsonConverter(typeof(JsonStringEnumConverter<ServiceTiming>))]
public enum ServiceTiming { OnTime = 1, Late = 2, Overdue = 3 }

public static class ServiceCatalog
{
    public static IReadOnlyDictionary<ServiceKind, string> Names { get; } =
        new Dictionary<ServiceKind, string>
        {
            [ServiceKind.EngineOil] = "Moottoriöljy ja öljynsuodatin",
            [ServiceKind.CabinFilter] = "Raitisilmasuodatin",
            [ServiceKind.AirFilter] = "Ilmansuodatin",
            [ServiceKind.BrakeFluid] = "Jarruneste",
            [ServiceKind.FrontBrakes] = "Etujarrut",
            [ServiceKind.RearBrakes] = "Takajarrut",
            [ServiceKind.SparkPlugs] = "Sytytystulpat",
            [ServiceKind.FuelFilter] = "Polttoainesuodatin",
            [ServiceKind.VehicleCheck] = "Ajoneuvon tarkastus"
        };

    public static string TimingName(ServiceTiming timing) => timing switch
    {
        ServiceTiming.OnTime => "Ajallaan",
        ServiceTiming.Late => "Myöhässä",
        ServiceTiming.Overdue => "Huoltoväli ylitetty",
        _ => throw new ArgumentOutOfRangeException(nameof(timing))
    };
}

public sealed record ServiceEntry
{
    [JsonRequired] public Guid Id { get; init; } = Guid.NewGuid();
    [JsonRequired] public DateOnly Date { get; init; }
    [JsonRequired] public int Mileage { get; init; }
    [JsonRequired] public ServiceKind[] Items { get; init; } = [];
    [JsonRequired] public ServiceTiming Timing { get; init; } = ServiceTiming.OnTime;
    [JsonRequired] public string Notes { get; init; } = "";
    [JsonRequired] public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonIgnore] public string DateLabel => Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    [JsonIgnore] public string MileageLabel => Mileage.ToString("N0", CultureInfo.GetCultureInfo("fi-FI")) + " km";
    [JsonIgnore] public string ItemsLabel => string.Join(" · ", Items.Select(i => ServiceCatalog.Names.GetValueOrDefault(i, "Tuntematon huolto")));
    [JsonIgnore] public string TimingLabel => ServiceCatalog.TimingName(Timing);
}

public sealed record ServiceHistory
{
    [JsonRequired] public int SchemaVersion { get; init; } = 1;
    [JsonRequired] public string Vehicle { get; init; } = "BMW G30 · 2018";
    [JsonRequired] public List<ServiceEntry> Entries { get; init; } = [];
}

public sealed class ValidationException(string message) : Exception(message);

public static class EntryValidation
{
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public static void Validate(ServiceEntry entry, DateOnly? today = null)
    {
        if (entry.Id == Guid.Empty) throw new ValidationException("Merkinnän tunniste puuttuu.");
        if (entry.Date < new DateOnly(2017, 1, 1) || entry.Date > (today ?? Today))
            throw new ValidationException("Huoltopäivän tulee olla välillä 1.1.2017–tänään.");
        if (entry.Mileage is < 0 or > 1_999_999)
            throw new ValidationException("Anna kilometrilukema väliltä 0–1 999 999.");
        if (entry.Items is null || entry.Items.Length == 0)
            throw new ValidationException("Valitse vähintään yksi tehty huolto.");
        if (entry.Items.Any(i => !Enum.IsDefined(i)) || entry.Items.Distinct().Count() != entry.Items.Length)
            throw new ValidationException("Huoltolista sisältää tuntemattoman tai toistuvan huollon.");
        if (!Enum.IsDefined(entry.Timing)) throw new ValidationException("Valitse huollon ajankohtaisuus.");
        if (entry.Notes is null || entry.Notes.Length > 4000)
            throw new ValidationException("Muistiinpanojen enimmäispituus on 4 000 merkkiä.");
        if (entry.CreatedAt == default) throw new ValidationException("Merkinnän tallennusaika puuttuu.");
    }

    public static void ValidateHistory(ServiceHistory history)
    {
        if (history.SchemaVersion != 1)
            throw new ValidationException("Huoltokirjan tiedostoversiota ei tueta.");
        if (history.Vehicle != "BMW G30 · 2018" || history.Entries is null)
            throw new ValidationException("Tämä tiedosto ei ole G30 (2018) -huoltokirja.");
        var ids = new HashSet<Guid>();
        foreach (var entry in history.Entries)
        {
            if (entry is null) throw new ValidationException("Tiedosto sisältää tyhjän merkinnän.");
            Validate(entry);
            if (!ids.Add(entry.Id)) throw new ValidationException("Tiedosto sisältää saman merkinnän kahdesti.");
        }
        int previousMax = -1;
        foreach (var day in history.Entries.GroupBy(e => e.Date).OrderBy(g => g.Key))
        {
            if (day.Min(e => e.Mileage) < previousMax)
                throw new ValidationException("Kilometrilukema pienenee myöhempänä huoltopäivänä. Tarkista päivä ja kilometrit.");
            previousMax = Math.Max(previousMax, day.Max(e => e.Mileage));
        }
    }
}
