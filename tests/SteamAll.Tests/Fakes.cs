// =====================================================================================
//  Fakes.cs
//  Gli "attori finti" delle prove:
//    FakePlatform  una piattaforma che finge Steam (aperto/chiuso), gli store e il Cestino, senza
//                  toccare niente di vero: ricorda solo cosa le è stato chiesto.
//    FakeHttp      una "rete" che risponde da sé: le prove non vanno mai su Internet.
// =====================================================================================

using System.Net;
using System.Text;
using SteamAll.Core.Platform;

namespace SteamAll.Tests;

public sealed class FakePlatform(string steamFolder) : IPlatform
{
    /// <summary>Il sistema che la piattaforma finge di essere (di solito Windows, come il PC delle prove).</summary>
    public OsKind Os { get; set; } = OsKind.Windows;

    public bool SteamRunning { get; set; } = true;
    public int Shutdowns { get; private set; }
    public int Starts { get; private set; }
    public List<string> RecycledFiles { get; } = [];
    public Dictionary<string, string> ProtocolExes { get; } = [];

    public string? FindSteamFolder() => steamFolder;
    public bool IsSteamRunning() => SteamRunning;

    public Task<bool> ShutdownSteamAsync(TimeSpan timeout)
    {
        Shutdowns++;
        SteamRunning = false;
        return Task.FromResult(true);   // un Task già finito, con il risultato "true"
    }

    public void StartSteam()
    {
        Starts++;
        SteamRunning = true;
    }

    public bool IsProtocolRegistered(string protocol) => ProtocolExes.ContainsKey(protocol);
    public string? ProtocolExe(string protocol) => ProtocolExes.GetValueOrDefault(protocol);
    public (string Exe, string ArgsPrefix) LinkOpener => (@"C:\WINDOWS\System32\rundll32.exe", "url.dll,FileProtocolHandler ");

    // Il "Cestino" finto: il file lo spostiamo in una cartella accanto, così la prova vede che non c'è più.
    public void MoveToRecycleBin(string path)
    {
        RecycledFiles.Add(path);
        string bin = Path.Combine(Path.GetDirectoryName(path)!, "..", "recycled");
        Directory.CreateDirectory(bin);
        string target = Path.Combine(bin, Guid.NewGuid().ToString("N") + Path.GetExtension(path));
        if (Directory.Exists(path)) Directory.Move(path, target);   // anche le cartelle (il tema vecchio)
        else File.Move(path, target);
    }

    // Niente cifratura vera: basta che "proteggere" e "sproteggere" siano uno il contrario dell'altro.
    public byte[] ProtectSecret(byte[] plain) => plain.Reverse().ToArray();
    public byte[] UnprotectSecret(byte[] protectedData) => protectedData.Reverse().ToArray();
}

/// <summary>
/// Un HttpMessageHandler finto: per ogni richiesta chiama "respond", che decide la risposta.
/// HttpClient usa un "handler" per mandare davvero le richieste: sostituendolo, niente va in rete.
/// </summary>
public sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!.ToString());
        return Task.FromResult(respond(request));
    }

    public static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Bytes(byte[] data) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };

    public static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound);

    /// <summary>Un PNG finto ma "vero" nell'intestazione: 3840x1240 (si leggono solo i primi byte).</summary>
    public static byte[] Png(int width, int height)
    {
        var png = new byte[64];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(png, 0);
        png[16] = (byte)(width >> 24); png[17] = (byte)(width >> 16); png[18] = (byte)(width >> 8); png[19] = (byte)width;
        png[20] = (byte)(height >> 24); png[21] = (byte)(height >> 16); png[22] = (byte)(height >> 8); png[23] = (byte)height;
        return png;
    }
}
