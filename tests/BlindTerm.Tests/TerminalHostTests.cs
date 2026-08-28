using System.Net;
using System.Net.Sockets;
using BlindTerm.App;
using BlindTerm.Core;
using BlindTerm.Core.Net;

namespace BlindTerm.Tests;

public class TerminalHostTests
{
    /// <summary>Runs posted window callbacks immediately while preserving their ordering.</summary>
    private sealed class ImmediateContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callback(state);
    }

    [Fact]
    public async Task AConnectionTakesOverTheShellAndReturnsToIt()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var host = new TerminalHost(120, 30, new ImmediateContext());
        string shell = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
        host.Start($"\"{shell}\" /d /q");

        Task<TcpClient> accepting = listener.AcceptTcpClientAsync();
        await host.ConnectOverAsync(new TelnetTarget("127.0.0.1", port));
        using TcpClient server = await accepting.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(TerminalSessionKind.Remote, host.Kind);
        Assert.False(host.CanConnectOver);

        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Updated += update =>
        {
            if (update.NewLines.Contains("Welcome back")) received.TrySetResult();
        };
        byte[] greeting = "Welcome back\r\n"u8.ToArray();
        await server.GetStream().WriteAsync(greeting);
        await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Exited += _ => disconnected.TrySetResult();
        server.Dispose();
        await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(host.ReturnToShell());
        Assert.Equal(TerminalSessionKind.Shell, host.Kind);
        Assert.True(host.IsRunning);
        Assert.True(host.CanConnectOver);
    }
}
