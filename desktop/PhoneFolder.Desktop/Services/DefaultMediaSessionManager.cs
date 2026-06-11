using PhoneFolder.Desktop.Models;
using System.Diagnostics;
using System.IO;

namespace PhoneFolder.Desktop.Services;

public static class DefaultMediaSessionManager
{
    private static readonly List<Session> Sessions = [];

    public static void Open(RemoteClient client, RemoteItem item)
    {
        var sessionClient = client.CreateSibling();
        var server = new RemoteMediaServer(sessionClient, item);
        var playlistPath = Path.Combine(
            Path.GetTempPath(),
            $"PhoneTransfer-{Guid.NewGuid():N}.m3u8");
        File.WriteAllText(
            playlistPath,
            $"#EXTM3U{Environment.NewLine}{server.StreamUri}{Environment.NewLine}");
        try
        {
            Process.Start(new ProcessStartInfo(playlistPath) { UseShellExecute = true });
            Sessions.Add(new Session(sessionClient, server, playlistPath));
        }
        catch
        {
            server.DisposeAsync().AsTask().GetAwaiter().GetResult();
            sessionClient.Dispose();
            File.Delete(playlistPath);
            throw;
        }
    }

    public static async Task DisposeAsync()
    {
        foreach (var session in Sessions.ToArray())
        {
            await session.Server.DisposeAsync();
            session.Client.Dispose();
            try
            {
                File.Delete(session.PlaylistPath);
            }
            catch
            {
            }
        }
        Sessions.Clear();
    }

    private sealed record Session(
        RemoteClient Client,
        RemoteMediaServer Server,
        string PlaylistPath);
}
