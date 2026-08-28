namespace BlindTerm.Core.Net;

/// <summary>
/// Everything needed to dial one host: where it is, and whether the connection is encrypted.
///
/// One value rather than a host and a port passed side by side, because TLS is a third thing
/// that has to travel with them the whole way -- from the menu, the command line and the
/// directory browser, through the window, to the socket. A pair that grew a third element
/// would have had to be unpacked and repacked at every step, and the step that forgot would
/// have connected in the clear without saying so.
/// </summary>
public sealed record TelnetTarget(string Host, int Port, bool UseTls = false)
{
    /// <summary>
    /// Whether to go ahead with a certificate that did not verify.
    ///
    /// Never set from a settings file or an address. It is only ever set by a person who has
    /// been shown what is wrong with the certificate and said to connect anyway, and it lasts
    /// for that one connection.
    /// </summary>
    public bool AllowUntrustedCertificate { get; init; }

    /// <summary>The written form, which round-trips through <see cref="TelnetAddress"/>.</summary>
    public string Address => TelnetAddress.Format(Host, Port, UseTls);

    /// <summary>How to say it out loud: an address alone does not say it is encrypted.</summary>
    public string Spoken => UseTls
        ? $"{Host} port {Port}, encrypted"
        : $"{Host} port {Port}";

    public override string ToString() => Address;

    public static bool TryParse(string? text, out TelnetTarget target)
    {
        target = new TelnetTarget(string.Empty, TelnetAddress.DefaultPort);
        if (!TelnetAddress.TryParse(text, out string host, out int port, out bool secure)) return false;
        target = new TelnetTarget(host, port, secure);
        return true;
    }
}
