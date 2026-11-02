using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using G30ServiceTool.Core;
using Microsoft.Win32;

namespace G30ServiceTool.App;

public partial class MainWindow : Window
{
    private readonly HistoryStore _store;
    private readonly Dictionary<ServiceKind, CheckBox> _choices = [];
    private readonly CancellationTokenSource _closing = new();
    private ServiceHistory _history = new();
    private bool _ready;

    public MainWindow() : this(HistoryStore.ForCurrentUser()) { }

    public MainWindow(HistoryStore store)
    {
        _store = store;
        InitializeComponent();
        DateBox.SelectedDate = DateTime.Today;
        DateBox.DisplayDateEnd = DateTime.Today;
        DateBox.DisplayDateStart = new DateTime(2017, 1, 1);
        foreach (var (kind, name) in ServiceCatalog.Names)
        {
            var check = new CheckBox { Content = name };
            System.Windows.Automation.AutomationProperties.SetName(check, name);
            _choices.Add(kind, check);
            ServiceChoices.Children.Add(check);
        }
        TimingBox.ItemsSource = Enum.GetValues<ServiceTiming>()
            .Select(t => new TimingOption(t, ServiceCatalog.TimingName(t))).ToArray();
        TimingBox.DisplayMemberPath = nameof(TimingOption.Label);
        TimingBox.SelectedIndex = 0;
        StoragePath.Text = _store.DirectoryPath;
        _ready = true;
        Reload();
        RefreshAdapters();
        Closed += (_, _) => { _closing.Cancel(); _closing.Dispose(); };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DateMileageError.Text = ItemsError.Text = FormMessage.Text = "";
        FormMessage.Foreground = Brushes.Firebrick;
        if (!DateTime.TryParse(DateBox.Text, CultureInfo.GetCultureInfo("fi-FI"), DateTimeStyles.None, out var date) ||
            date.Date < new DateTime(2017, 1, 1) || date.Date > DateTime.Today)
        {
            DateMileageError.Text = "Anna huoltopäivä muodossa pp.kk.vvvv väliltä 1.1.2017–tänään.";
            DateBox.Focus();
            return;
        }
        // Spaces used as grouping separators are accepted; decimals and signs are not.
        var mileageText = MileageBox.Text.Replace(" ", "").Replace("\u00a0", "").Replace("\u202f", "");
        if (!int.TryParse(mileageText, NumberStyles.None, CultureInfo.InvariantCulture, out var mileage) || mileage > 1_999_999)
        {
            DateMileageError.Text = "Anna kokonainen kilometrilukema väliltä 0–1 999 999.";
            MileageBox.Focus();
            return;
        }
        var selected = _choices.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToArray();
        if (selected.Length == 0)
        {
            ItemsError.Text = "Valitse vähintään yksi tehty huolto.";
            _choices.First().Value.Focus();
            return;
        }
        var entry = new ServiceEntry
        {
            Date = DateOnly.FromDateTime(date), Mileage = mileage, Items = selected,
            Timing = ((TimingOption)TimingBox.SelectedItem).Value, Notes = NotesBox.Text.Trim()
        };
        Guard(() =>
        {
            _history = _store.Add(entry);
            RefreshHistory(entry.Id);
            foreach (var check in _choices.Values) check.IsChecked = false;
            NotesBox.Clear();
            // Clear mileage too: an accidental second click cannot create the same entry.
            MileageBox.Clear();
            FormMessage.Foreground = Brushes.DarkGreen;
            FormMessage.Text = "Tallennettu tietokoneelle. Auton tietoja ei muutettu.";
            StatusText.Text = "Huoltomerkintä tallennettu · Aiempi paikallinen historia varmuuskopioidaan ennen muutoksia.";
        }, error => FormMessage.Text = error);
    }

    private void Reload() => Guard(() =>
    {
        var selectedId = (HistoryList.SelectedItem as ServiceEntry)?.Id;
        _history = _store.Load();
        SaveButton.IsEnabled = true;
        RefreshHistory(selectedId);
        StatusText.Text = "Huoltokirja ladattu · " + _history.Entries.Count + " paikallista merkintää.";
    }, error =>
    {
        SaveButton.IsEnabled = false;
        StatusText.Text = "Huoltokirjan avaaminen epäonnistui: " + error;
    });

    private void RefreshHistory(Guid? selectedId)
    {
        HistoryList.ItemsSource = _history.Entries.OrderByDescending(e => e.Date).ThenByDescending(e => e.Mileage).ThenByDescending(e => e.CreatedAt).ToArray();
        EntryCount.Text = _history.Entries.Count.ToString(CultureInfo.InvariantCulture);
        LatestMileage.Text = _history.Entries.Count == 0 ? "—" : _history.Entries.MaxBy(e => e.Mileage)!.MileageLabel;
        EmptyState.Visibility = _history.Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryList.SelectedItem = _history.Entries.FirstOrDefault(e => e.Id == selectedId);
    }

    private void History_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        var entry = HistoryList.SelectedItem as ServiceEntry;
        SelectedNotes.Text = entry is null ? "Valitse merkintä nähdäksesi muistiinpanot." :
            string.IsNullOrWhiteSpace(entry.Notes) ? "Ei muistiinpanoja. Tekijä: itse tehty huolto." : "Itse tehty huolto\n" + entry.Notes;
        ShowDraftButton.IsEnabled = entry is not null;
        UpdateDraft();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();
    private void ShowDraft_Click(object sender, RoutedEventArgs e) { MainTabs.SelectedIndex = 2; UpdateDraft(); }
    private void Workshop_Changed(object sender, TextChangedEventArgs e) { if (_ready) UpdateDraft(); }

    private void UpdateDraft()
    {
        ExportDraftButton.IsEnabled = false;
        if (HistoryList.SelectedItem is not ServiceEntry entry)
        {
            DraftEntryLabel.Text = "Merkintää ei ole valittu.";
            DraftPreview.Text = "Valitse tallennettu merkintä Huoltokirja-välilehdeltä.";
            return;
        }
        DraftEntryLabel.Text = entry.DateLabel + " · " + entry.MileageLabel;
        try
        {
            DraftPreview.Text = Tool32Draft.Build(entry, WorkshopBox.Text);
            ExportDraftButton.IsEnabled = true;
        }
        catch (ValidationException ex) { DraftPreview.Text = ex.Message; }
    }

    private void ExportDraft_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not ServiceEntry entry) return;
        var dialog = new SaveFileDialog { Title = "Tallenna tarkasteltava Tool32-luonnos", Filter = "Tekstiluonnos (*.txt)|*.txt", DefaultExt = ".txt", AddExtension = true, FileName = $"g30-{entry.Date:yyyy-MM-dd}.tool32-draft.txt" };
        if (dialog.ShowDialog(this) != true) return;
        Guard(() =>
        {
            Tool32Draft.Save(dialog.FileName, Tool32Draft.Build(entry, WorkshopBox.Text));
            StatusText.Text = "Tool32-tarkasteluluonnos tallennettu: " + dialog.FileName;
        });
    }

    private async void CheckConnection_Click(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        ConnectionMessage.Text = "Tarkistetaan TCP-yhteyttä…";
        try
        {
            if (!int.TryParse(PortBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var port))
                throw new ValidationException("Anna TCP-portti kokonaislukuna väliltä 1–65535.");
            var result = await EnetConnection.CheckAsync(HostBox.Text.Trim(), port, cancellationToken: _closing.Token);
            ConnectionMessage.Text = result.Message;
        }
        catch (ValidationException ex) { ConnectionMessage.Text = ex.Message; }
        catch (OperationCanceledException) { /* Window closed. */ }
        catch (IOException ex) { ConnectionMessage.Text = "Yhteyden tarkistus epäonnistui: " + ex.Message; }
        finally { CheckButton.IsEnabled = true; }
    }

    private void RefreshAdapters()
    {
        try
        {
            var adapters = EnetConnection.LocalAdapters();
            AdaptersText.Text = adapters.Length == 0 ? "Aktiivisia IPv4-verkkosovittimia ei löytynyt." : string.Join("\n", adapters);
        }
        catch (NetworkInformationException ex) { AdaptersText.Text = "Sovittimien luku epäonnistui: " + ex.Message; }
    }
    private void Adapters_Click(object sender, RoutedEventArgs e) => RefreshAdapters();

    private void BrowseTool32_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Valitse tällä läppärillä oleva Tool32.exe", Filter = "Tool32 (Tool32.exe)|Tool32.exe" };
        if (dialog.ShowDialog(this) == true) Tool32PathBox.Text = dialog.FileName;
    }

    private void BrowseEcu_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Valitse diagnostiikan ECU-kansio" };
        if (dialog.ShowDialog(this) == true) EcuPathBox.Text = dialog.FolderName;
    }

    private void InspectInstallation_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        InstallationReport.Text = EdiabasInspection.Inspect(Tool32PathBox.Text, EcuPathBox.Text).ToText();
        StatusText.Text = "Paikalliset Tool32-tiedostot tarkistettu. Autoyhteyttä ei avattu.";
    }, error => InstallationReport.Text = error);

    private void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "Vie paikallinen huoltokirja", Filter = "Huoltokirja (*.json)|*.json", DefaultExt = ".json", AddExtension = true, FileName = $"g30-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json" };
        if (dialog.ShowDialog(this) != true) return;
        Guard(() => { _store.ExportBackup(dialog.FileName); StatusText.Text = "Varmuuskopio viety: " + dialog.FileName; });
    }

    private void ImportBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Yhdistä paikallisen huoltokirjan varmuuskopio", Filter = "Huoltokirja (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        Guard(() =>
        {
            _history = _store.MergeBackup(dialog.FileName);
            RefreshHistory(null);
            SaveButton.IsEnabled = true;
            StatusText.Text = "Varmuuskopio yhdistetty. Nykyiset merkinnät säilytettiin.";
        });
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        Directory.CreateDirectory(_store.DirectoryPath);
        Process.Start(new ProcessStartInfo(_store.DirectoryPath) { UseShellExecute = true });
    });

    private void Guard(Action action, Action<string>? onError = null)
    {
        try { action(); }
        catch (Exception ex) when (ex is ValidationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            StatusText.Text = ex.Message;
            onError?.Invoke(ex.Message);
        }
    }

    private sealed record TimingOption(ServiceTiming Value, string Label);
}
