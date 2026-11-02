using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace G30ServiceTool.Core;

public sealed record ConnectionResult(bool Reachable, string Message);

public static class EnetConnection
{
    public static string[] LocalAdapters() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => $"{n.Name} · {a.Address}"))
        .ToArray();

    public static async Task<ConnectionResult> CheckAsync(string host, int port = 6801,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IPAddress.TryParse(host, out var address) || address.AddressFamily != AddressFamily.InterNetwork ||
            host.Split('.').Length != 4 || address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast) ||
            address.GetAddressBytes()[0] is >= 224 or 0)
            throw new ValidationException("Anna auton kelvollinen IPv4-osoite, esimerkiksi 169.254.x.x. Osoitetta ei arvata.");
        if (port is < 1 or > 65535) throw new ValidationException("Portin tulee olla välillä 1–65535.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(3));
        using var client = new TcpClient(AddressFamily.InterNetwork);
        try
        {
            await client.ConnectAsync(address, port, deadline.Token);
            // Deliberately send no HSFZ, DoIP, UDS or EDIABAS application data.
            return new(true, $"{address}:{port} vastasi TCP-yhteyteen. Autoa tai NBT EVO -yksikköä ei tunnistettu.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, "Yhteyden aikaraja ylittyi. Tarkista IP-osoite, kaapeli ja auton virrat.");
        }
        catch (SocketException ex)
        {
            return new(false, $"TCP-yhteys ei onnistunut ({ex.SocketErrorCode}). Tarkista osoite, portti ja verkkosovitin.");
        }
    }
}
