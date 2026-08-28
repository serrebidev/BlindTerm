using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace BlindTerm.Core.Net;

/// <summary>
/// The host's certificate did not check out.
///
/// Its own class because this is the one connection failure with a sensible second move: a
/// MUD running on a self-signed certificate is common and not in itself an attack, and the
/// person dialling it is entitled to be told exactly what is wrong and to decide. The
/// alternative is what every other client does -- either refuse with "authentication failed",
/// which says nothing, or verify nothing at all, which makes the encryption decorative.
///
/// What .NET raises instead is an AuthenticationException whose reason is buried two
/// exceptions down and reads like a Win32 error code, which is no use to anyone and much less
/// use read aloud.
/// </summary>
public sealed class TelnetCertificateException : Exception
{
    public TelnetCertificateException(TelnetTarget target, SslPolicyErrors errors,
        X509Certificate2? certificate, Exception? inner = null)
        : base(Explain(target, errors, certificate), inner)
    {
        ArgumentNullException.ThrowIfNull(target);
        Target = target;
        Errors = errors;
        Subject = certificate?.Subject ?? string.Empty;
        Issuer = certificate?.Issuer ?? string.Empty;
        Expires = certificate?.NotAfter;
        Fingerprint = certificate?.Thumbprint ?? string.Empty;
    }

    public TelnetTarget Target { get; }
    public SslPolicyErrors Errors { get; }
    public string Subject { get; }
    public string Issuer { get; }
    public DateTime? Expires { get; }

    /// <summary>The SHA-1 thumbprint, which is what a MUD publishes when it publishes one.</summary>
    public string Fingerprint { get; }

    /// <summary>The same connection, with the certificate accepted. Only ever built after asking.</summary>
    public TelnetTarget Anyway => Target with { AllowUntrustedCertificate = true };

    private static string Explain(TelnetTarget target, SslPolicyErrors errors,
        X509Certificate2? certificate)
    {
        var lines = new List<string>
        {
            $"BlindTerm could not verify the certificate {target.Host} presented.",
            string.Empty,
        };

        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
            lines.Add("The host offered no certificate at all. This port may not be the encrypted one.");
        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
            lines.Add($"The certificate is not for {target.Host}.");
        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors))
            lines.Add("The certificate was not signed by an authority this computer trusts. "
                      + "A MUD that signed its own is the usual reason, and so is one that expired.");

        if (certificate is not null)
        {
            lines.Add(string.Empty);
            lines.Add("Issued to: " + Name(certificate.Subject));
            lines.Add("Issued by: " + Name(certificate.Issuer));
            lines.Add("Expires: " + certificate.NotAfter.ToString("d MMMM yyyy"));
            lines.Add("Fingerprint: " + Spaced(certificate.Thumbprint));
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>The common name out of a distinguished name, which is the readable part.</summary>
    private static string Name(string distinguished)
    {
        foreach (string part in distinguished.Split(','))
        {
            string trimmed = part.Trim();
            if (trimmed.StartsWith("CN=", StringComparison.OrdinalIgnoreCase)) return trimmed[3..];
        }
        return distinguished;
    }

    /// <summary>
    /// Four characters at a time. A forty-character hex run is read out as one unbroken
    /// stream of letters otherwise, which cannot be compared against anything.
    /// </summary>
    private static string Spaced(string fingerprint)
    {
        var parts = new List<string>();
        for (int at = 0; at < fingerprint.Length; at += 4)
            parts.Add(fingerprint[at..Math.Min(at + 4, fingerprint.Length)]);
        return string.Join(' ', parts);
    }
}
