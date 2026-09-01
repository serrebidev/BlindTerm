using System.Net;
using System.Net.Sockets;
using System.Text;
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

    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _work = new();
        public int Posts { get; private set; }

        public override void Post(SendOrPostCallback callback, object? state)
        {
            Posts++;
            _work.Enqueue((callback, state));
        }

        public void RunNext()
        {
            var work = _work.Dequeue();
            work.Callback(work.State);
        }
    }

    [Fact]
    public void RapidUpdatesShareOneUiPostWithoutLosingTheirOrder()
    {
        var context = new QueuedContext();
        using var host = new TerminalHost(80, 25, context);
        var seen = new List<string>();
        host.Updated += update => seen.AddRange(update.NewLines);

        host.AppendExternal(["first"]);
        host.AppendExternal(["second"]);

        Assert.Equal(1, context.Posts);
        Assert.Empty(seen);

        context.RunNext();

        Assert.Equal(["first", "second"], seen);
        Assert.Equal(1, context.Posts);
    }

    [Fact]
    public void RapidLineOutputBecomesOneUiBatchWithoutLosingLines()
    {
        var context = new QueuedContext();
        using var host = new TerminalHost(80, 25, context);
        var batches = new List<TerminalUpdate>();
        host.Updated += batches.Add;

        host.Core.Feed(Encoding.UTF8.GetBytes("first\r\n"));
        host.Core.Feed(Encoding.UTF8.GetBytes("second\r\n"));

        Assert.Equal(1, context.Posts);
        Assert.Empty(batches);

        context.RunNext();

        TerminalUpdate batch = Assert.Single(batches);
        Assert.Equal(["first", "second"], batch.NewLines);
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
