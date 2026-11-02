using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using G30ServiceTool.App;
using G30ServiceTool.Core;

namespace G30ServiceTool.Tests;

internal static class Program
{
    private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "G30ServiceTool.Tests", Guid.NewGuid().ToString("N"));
    private static int _passed;
    private static int _failed;

    [STAThread]
    private static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fi-FI");
        Directory.CreateDirectory(TempRoot);
        Run("Validation rejects future dates, empty jobs and invalid values", Validation);
        Run("Atomic persistence, restart and exact previous-version backup", Persistence);
        Run("Corrupt or incomplete history is never overwritten", Corruption);
        Run("Chronology permits backdated entries and rejects decreasing odometer", Chronology);
        Run("Two store instances preserve each other's entries; lock failure preserves file", Concurrency);
        Run("Backup merge is additive, idempotent and rejects ID conflicts", BackupMerge);
        Run("Backup failures leave the primary file untouched", BackupFailure);
        Run("Tool32 draft has reference fields, DIY flag, no erase and no executable export", Draft);
        Run("ENET loopback accepts TCP but receives zero application bytes", Network);
        Run("ENET rejects invalid targets and honors cancellation", InvalidNetwork);
        Run("WPF form saves, reloads, validates and previews drafts", () => Ui(args.FirstOrDefault()));
        Console.WriteLine($"\n{_passed} passed; {_failed} failed.");
        // Only our uniquely named test directory is removed.
        Directory.Delete(TempRoot, recursive: true);
        return _failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try { test(); _passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { _failed++; Console.Error.WriteLine("FAIL " + name + "\n" + ex); }
    }

    private static HistoryStore Store(string name) => new(Path.Combine(TempRoot, name));
    private static ServiceEntry Entry(int mileage = 100000, int day = 1) => new()
    {
        Date = new DateOnly(2024, 6, day), Mileage = mileage,
        Items = [ServiceKind.EngineOil, ServiceKind.CabinFilter], Notes = "Öljy 5W-30 · 5,0 l"
    };
    private static void Assert(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private static void Validation()
    {
        var valid = Entry();
        EntryValidation.Validate(valid);
        Throws<ValidationException>(() => EntryValidation.Validate(valid with { Date = EntryValidation.Today.AddDays(1) }));
        Throws<ValidationException>(() => EntryValidation.Validate(valid with { Mileage = -1 }));
        Throws<ValidationException>(() => EntryValidation.Validate(valid with { Items = [] }));
        Throws<ValidationException>(() => EntryValidation.Validate(valid with { Items = [ServiceKind.EngineOil, ServiceKind.EngineOil] }));
        Throws<ValidationException>(() => EntryValidation.Validate(valid with { Items = [(ServiceKind)999] }));
        Throws<ValidationException>(() => EntryValidation.Validate(valid with { Timing = (ServiceTiming)99 }));
        Throws<ValidationException>(() => EntryValidation.Validate(valid with { Notes = new string('a', 4001) }));
    }

    private static void Persistence()
    {
        var store = Store("persist");
        Assert(store.Load().Entries.Count == 0);
        var first = Entry();
        store.Add(first);
        var before = File.ReadAllBytes(store.FilePath);
        store.Add(Entry(110000, 2));
        var reopened = new HistoryStore(store.DirectoryPath).Load();
        Assert(reopened.Entries.Count == 2 && reopened.Entries[0].Notes == first.Notes);
        var backup = Directory.GetFiles(store.BackupDirectory).Single();
        Assert(before.SequenceEqual(File.ReadAllBytes(backup)), "Backup must match previous file exactly");
        Assert(Directory.GetFiles(store.DirectoryPath, "*.tmp").Length == 0);
        store.ExportBackup(Path.Combine(store.DirectoryPath, "export.json"));
        Assert(HistoryStore.Read(Path.Combine(store.DirectoryPath, "export.json")).Entries.Count == 2);
        Throws<ValidationException>(() => store.ExportBackup(store.FilePath));
    }

    private static void Corruption()
    {
        var store = Store("corrupt");
        Directory.CreateDirectory(store.DirectoryPath);
        foreach (var contents in new[] { "{broken", "{}", "null", "{\"SchemaVersion\":99,\"Vehicle\":\"BMW G30 · 2018\",\"Entries\":[]}" })
        {
            File.WriteAllText(store.FilePath, contents);
            Throws<ValidationException>(() => store.Add(Entry()));
            Assert(File.ReadAllText(store.FilePath) == contents);
        }
        var json = JsonSerializer.Serialize(new ServiceHistory { Entries = [Entry()] });
        File.WriteAllText(store.FilePath, json.Replace("\"Mileage\":100000,", ""));
        Throws<ValidationException>(() => store.Load());
    }

    private static void Chronology()
    {
        var store = Store("chronology");
        store.Add(Entry(120000, 3));
        store.Add(Entry(100000, 1));
        var before = File.ReadAllText(store.FilePath);
        Throws<ValidationException>(() => store.Add(Entry(90000, 2)));
        Assert(File.ReadAllText(store.FilePath) == before);
        store.Add(Entry(110000, 2));
        Assert(store.Load().Entries.Count == 3);
    }

    private static void Concurrency()
    {
        var first = Store("concurrent");
        var second = new HistoryStore(first.DirectoryPath);
        first.Add(Entry());
        second.Add(Entry(110000, 2));
        first.Add(Entry(120000, 3));
        Assert(second.Load().Entries.Count == 3);
        var before = File.ReadAllText(first.FilePath);
        using var heldLock = new FileStream(Path.Combine(first.DirectoryPath, ".history.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Throws<IOException>(() => second.Add(Entry(130000, 4)));
        Assert(File.ReadAllText(first.FilePath) == before);
    }

    private static void BackupMerge()
    {
        var store = Store("merge");
        var first = Entry();
        store.Add(first);
        var imported = Store("imported");
        imported.Add(first);
        imported.Add(Entry(110000, 2));
        Assert(store.MergeBackup(imported.FilePath).Entries.Count == 2);
        Assert(store.MergeBackup(imported.FilePath).Entries.Count == 2);
        var conflict = Path.Combine(TempRoot, "conflict.json");
        File.WriteAllText(conflict, JsonSerializer.Serialize(new ServiceHistory { Entries = [first with { Notes = "Changed" }] }));
        var before = File.ReadAllText(store.FilePath);
        Throws<ValidationException>(() => store.MergeBackup(conflict));
        Assert(File.ReadAllText(store.FilePath) == before);
    }

    private static void BackupFailure()
    {
        var store = Store("backup-failure");
        store.Add(Entry());
        var before = File.ReadAllText(store.FilePath);
        File.WriteAllText(store.BackupDirectory, "Blocks directory creation");
        Throws<IOException>(() => store.Add(Entry(110000, 2)));
        Assert(File.ReadAllText(store.FilePath) == before);
    }

    private static void Draft()
    {
        var draft = Tool32Draft.Build(Entry(), "00181");
        Assert(draft.Contains("1|g_mmi|steuern_servicehistory_add||1;6;2024;0;100000;0;00181;0x1;2;1;0x1;0;0;4;0x1;0;0"));
        Assert(!draft.Contains("steuern_servicehistory_erase", StringComparison.OrdinalIgnoreCase));
        Assert(draft.Contains("NBT EVO -yhteensopivuutta ei ole vahvistettu"));
        foreach (var code in new[] { "", "1;2", "123456789", "1\n", "１２３", "abc" })
            Throws<ValidationException>(() => Tool32Draft.Build(Entry(), code));
        Throws<ValidationException>(() => Tool32Draft.Save(Path.Combine(TempRoot, "test.tst"), draft));
        var output = Path.Combine(TempRoot, "draft.txt");
        Tool32Draft.Save(output, draft);
        Assert(File.ReadAllText(output) == draft);
    }

    private static void Network()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var accept = listener.AcceptTcpClientAsync();
            var result = EnetConnection.CheckAsync("127.0.0.1", port).GetAwaiter().GetResult();
            using var peer = accept.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            peer.ReceiveTimeout = 3000;
            Assert(result.Reachable);
            Assert(peer.GetStream().ReadByte() == -1, "The connection test must send zero application bytes");
            Assert(result.Message.Contains("ei tunnistettu"));
        }
        finally { listener.Stop(); }
        var closedPort = ((IPEndPoint)GetClosedEndpoint()).Port;
        Assert(!EnetConnection.CheckAsync("127.0.0.1", closedPort).GetAwaiter().GetResult().Reachable);
    }

    private static EndPoint GetClosedEndpoint()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = listener.LocalEndpoint;
        listener.Stop();
        return endpoint;
    }

    private static void InvalidNetwork()
    {
        foreach (var target in new[] { "auto", "", "localhost", "127.1", "::1", "0.0.0.0", "255.255.255.255", "224.0.0.1", "999.1.1.1" })
            Throws<ValidationException>(() => EnetConnection.CheckAsync(target).GetAwaiter().GetResult());
        Throws<ValidationException>(() => EnetConnection.CheckAsync("127.0.0.1", 0).GetAwaiter().GetResult());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Throws<OperationCanceledException>(() => EnetConnection.CheckAsync("127.0.0.1", cancellationToken: cancellation.Token).GetAwaiter().GetResult());
    }

    private static void Ui(string? screenshotDirectory)
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/G30ServiceTool;component/Theme.xaml", UriKind.Absolute)
        });
        var store = Store("ui");
        var window = new MainWindow(store);
        window.Show();
        window.UpdateLayout();
        T Find<T>(string name) where T : FrameworkElement => (T)window.FindName(name);
        void Click(string name) => Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            Find<TextBox>("MileageBox").Text = "not a number";
            Click("SaveButton");
            Assert(Find<TextBlock>("DateMileageError").Text.Length > 0 && store.Load().Entries.Count == 0);
            Find<TextBox>("MileageBox").Text = "185 000";
            Click("SaveButton");
            Assert(Find<TextBlock>("ItemsError").Text.Length > 0);
            var jobs = Find<StackPanel>("ServiceChoices").Children.OfType<CheckBox>().ToArray();
            jobs[0].IsChecked = jobs[1].IsChecked = true;
            Find<TextBox>("NotesBox").Text = "Testimerkintä · 5W-30 / 5,0 l";
            Click("SaveButton");
            Assert(store.Load().Entries.Count == 1);
            Assert(Find<ListBox>("HistoryList").SelectedItem is ServiceEntry);
            Assert(Find<TextBlock>("EntryCount").Text == "1");
            Find<TextBox>("WorkshopBox").Text = "00181";
            Assert(Find<Button>("ExportDraftButton").IsEnabled);
            Assert(Find<TextBox>("DraftPreview").Text.Contains("steuern_servicehistory_add"));
            Click("RefreshButton");
            Assert(Find<ListBox>("HistoryList").Items.Count == 1);
            Click("SaveButton");
            Assert(store.Load().Entries.Count == 1, "Accidental second save must not duplicate the entry");
            Find<TextBox>("MileageBox").Text = "185000";
            jobs[0].IsChecked = jobs[1].IsChecked = true;
            Find<TextBox>("NotesBox").Text = "Esimerkki: öljy ja suodattimet vaihdettu.";
            Find<TextBlock>("DateMileageError").Text = "";
            if (screenshotDirectory is not null)
            {
                Directory.CreateDirectory(screenshotDirectory);
                Capture(window, Path.Combine(screenshotDirectory, "huoltokirja.png"));
                Find<TabControl>("MainTabs").SelectedIndex = 2;
                Capture(window, Path.Combine(screenshotDirectory, "tool32-luonnos.png"));
                Find<TabControl>("MainTabs").SelectedIndex = 1;
                Capture(window, Path.Combine(screenshotDirectory, "enet.png"));
                Find<TabControl>("MainTabs").SelectedIndex = 3;
                Capture(window, Path.Combine(screenshotDirectory, "varmuuskopiot.png"));
                window.Width = 980; window.Height = 650;
                Find<TabControl>("MainTabs").SelectedIndex = 0;
                Capture(window, Path.Combine(screenshotDirectory, "compact.png"));
            }
        }
        finally { window.Close(); }
        var reopened = new MainWindow(store);
        Assert(((ListBox)reopened.FindName("HistoryList")).Items.Count == 1);
        reopened.Close();
        var corruptStore = Store("ui-corrupt");
        Directory.CreateDirectory(corruptStore.DirectoryPath);
        File.WriteAllText(corruptStore.FilePath, "broken");
        var corruptWindow = new MainWindow(corruptStore);
        Assert(!((Button)corruptWindow.FindName("SaveButton")).IsEnabled);
        corruptWindow.Close();
        application.Shutdown();
    }

    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
