using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BlindTerm.App;
using BlindTerm.Core.Net;

namespace BlindTerm.Tests;

/// <summary>
/// Encrypted telnet: reading it out of an address, carrying it through, and speaking it.
///
/// A MUD that offers both puts the encrypted service on a different port, so "which port" and
/// "encrypted or not" are one answer. Anything that carries the first without the second
/// connects to the wrong service, or connects to the right one in the clear.
/// </summary>
public class TelnetSecurityTests
{
    [Theory]
    [InlineData("ssl://coremud.org:4022", "coremud.org", 4022)]
    [InlineData("tls://coremud.org:4022", "coremud.org", 4022)]
    [InlineData("telnets://coremud.org:4022", "coremud.org", 4022)]
    [InlineData("SSL://CoreMUD.org:4022", "CoreMUD.org", 4022)]
    [InlineData("ssl://coremud.org", "coremud.org", 23)]
    [InlineData("ssl://[2001:db8::1]:4022", "2001:db8::1", 4022)]
    public void AnEncryptedAddressSaysSo(string written, string host, int port)
    {
        Assert.True(TelnetAddress.TryParse(written, out string parsed, out int parsedPort, out bool secure));
        Assert.Equal(host, parsed);
        Assert.Equal(port, parsedPort);
        Assert.True(secure);
    }

    [Theory]
    [InlineData("coremud.org:4000")]
    [InlineData("telnet://coremud.org:4000")]
    [InlineData("coremud.org")]
    public void APlainAddressIsNotQuietlyUpgraded(string written)
    {
        Assert.True(TelnetAddress.TryParse(written, out _, out _, out bool secure));
        Assert.False(secure);
    }

    [Fact]
    public void AnEncryptedAddressSurvivesBeingWrittenDownAndReadBack()
    {
        // This is what a remembered address goes through. Losing the scheme here would bring
        // an entry back as the plain service on the encrypted port.
        string written = TelnetAddress.Format("coremud.org", 4022, secure: true);
        Assert.Equal("ssl://coremud.org:4022", written);

        Assert.True(TelnetTarget.TryParse(written, out TelnetTarget target));
        Assert.Equal(new TelnetTarget("coremud.org", 4022, UseTls: true), target);
        Assert.Equal(written, target.Address);
        Assert.Equal("coremud.org port 4022, encrypted", target.Spoken);
    }

    [Fact]
    public void ThePortIsStillNotWrittenDownWhenItIsTheDefault()
    {
        Assert.Equal("coremud.org", TelnetAddress.Format("coremud.org", 23, secure: false));
        Assert.Equal("ssl://coremud.org", TelnetAddress.Format("coremud.org", 23, secure: true));
    }

    [Theory]
    [InlineData("telnet ssl://coremud.org 4022")]
    [InlineData("telnet ssl://coremud.org:4022")]
    public void ATypedDialCanAskForEncryption(string command)
    {
        TelnetTarget? parsed = TelnetCommand.Parse(command);

        Assert.NotNull(parsed);
        Assert.Equal("coremud.org", parsed!.Host);
        Assert.Equal(4022, parsed.Port);
        Assert.True(parsed.UseTls);
    }

    [Fact]
    public void TheCommandLineCanAskForEncryption()
    {
        TelnetTarget? parsed = Program.TelnetArgument(["--telnet", "ssl://coremud.org", "4022"]);

        Assert.NotNull(parsed);
        Assert.Equal("coremud.org", parsed!.Host);
        // The scheme is full of colons and none of them is a port, so the separate one wins.
        Assert.Equal(4022, parsed.Port);
        Assert.True(parsed.UseTls);
    }

    [Fact]
    public async Task AnEncryptedConnectionReportsWhatWasNegotiated()
    {
        using X509Certificate2 certificate = SelfSigned("localhost");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var typed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task serving = Serve(listener, certificate, typed);

        using var session = new TelnetSession();
        // Self-signed, so it does not verify; accepting it is the branch a person reaches by
        // being asked and saying yes.
        var target = new TelnetTarget("localhost", port, UseTls: true) { AllowUntrustedCertificate = true };
        await session.ConnectAsync(target, 120, 30);

        Assert.True(session.IsSecure);
        Assert.StartsWith("TLS", session.Security);

        var arrived = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Output += memory => arrived.TrySetResult(System.Text.Encoding.UTF8.GetString(memory.Span));
        session.Begin();

        string text = await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("hello", text);

        // Reading and writing happen on two threads over the one stream, which is the thing
        // wrapping a socket in TLS could have broken without breaking the handshake.
        session.Write("who\r\n");
        Assert.Equal("who\r\n", await typed.Task.WaitAsync(TimeSpan.FromSeconds(10)));

        await serving.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ACertificateThatDoesNotVerifyIsExplainedRatherThanRefused()
    {
        using X509Certificate2 certificate = SelfSigned("localhost");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Task serving = Serve(listener, certificate);

        using var session = new TelnetSession();
        var failure = await Assert.ThrowsAsync<TelnetCertificateException>(
            () => session.ConnectAsync(new TelnetTarget("localhost", port, UseTls: true), 120, 30));

        // The whole objection, in words, rather than a Win32 code nested two exceptions down.
        Assert.Contains("could not verify the certificate", failure.Message);
        Assert.Contains("not signed by an authority this computer trusts", failure.Message);
        Assert.Contains("localhost", failure.Subject);
        Assert.True(failure.Anyway.AllowUntrustedCertificate);
        Assert.False(failure.Target.AllowUntrustedCertificate);

        // A fingerprint that cannot be compared is not a fingerprint, so it is grouped.
        Assert.Contains(" ", failure.Message[failure.Message.IndexOf("Fingerprint:", StringComparison.Ordinal)..]);

        try { await serving.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (Exception ex) when (ex is IOException or AggregateException or AuthenticationException) { }
    }

    /// <summary>Accepts one connection, speaks TLS over it, says hello and reports a reply.</summary>
    private static async Task Serve(TcpListener listener, X509Certificate2 certificate,
        TaskCompletionSource<string>? heard = null)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync();
        await using var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        await ssl.AuthenticateAsServerAsync(certificate);
        await ssl.WriteAsync("hello\r\n"u8.ToArray());
        await ssl.FlushAsync();

        if (heard is null)
        {
            // Long enough for the reading thread to take it, short enough not to hold up a run.
            await Task.Delay(200);
            return;
        }

        var buffer = new byte[64];
        int read = await ssl.ReadAsync(buffer);
        heard.TrySetResult(System.Text.Encoding.UTF8.GetString(buffer, 0, read));
    }

    private static X509Certificate2 SelfSigned(string name)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        var alternatives = new SubjectAlternativeNameBuilder();
        alternatives.AddDnsName(name);
        request.CertificateExtensions.Add(alternatives.Build());
        X509Certificate2 certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // Windows will only use a certificate for a server handshake from a keyed store copy.
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null,
            X509KeyStorageFlags.Exportable);
    }
}
