using System.Text;

namespace BlindTerm.Core.Net;

/// <summary>
/// The TELNET option layer (RFC 854 and friends), separated from any socket so it can be
/// tested a byte at a time.
///
/// A telnet stream is text with commands wedged into it, introduced by IAC. Everything here
/// does two things: hand the text on untouched, and answer the commands. What it answers is
/// the interesting part.
///
/// BlindTerm refuses every option that would put something in the text that the text cannot
/// carry: the compression options, which make a stream this terminal cannot read, and MXP,
/// whose markup ends up spoken in the middle of a sentence by a client that does not render
/// it. MSDP, ATCP and ZMP are refused as duplicates of a channel already agreed below.
///
/// It accepts the three that hand over facts rather than markup.
///
/// The MUD Sound Protocol is what a MUD asks about before it will send a sound at all. Its
/// triggers arrive as text and are lifted out of the stream by <see cref="MspScanner"/>;
/// agreeing costs nothing even with sound switched off, because a trigger left in the text is
/// a line read aloud in the middle of a fight.
///
/// GMCP carries structured messages beside the text: which room this is, what its exits are,
/// how the character is doing. For a player who cannot see the screen that is the difference
/// between being told the exits and having to find the word "Exits" in a paragraph. It is a
/// subscription, so agreeing to it is not enough -- see <see cref="IntroduceOverGmcp"/>.
///
/// MSSP is the server describing itself, and is read-only.
///
/// Nothing is offered unsolicited. A telnet client that announces itself the moment it
/// connects puts three commands in front of whatever the far end was expecting, which a MUD
/// shrugs off and a plain TCP service -- a mail server, a web server, the sort of thing a
/// telnet client is also used to poke at -- reads as the first three bytes of a request.
/// Every option below is therefore an answer to a question.
///
/// It does answer TERMINAL-TYPE, and that answer carries the point of the exercise. The MUD
/// convention is a cycle of three replies -- client name, terminal type, then an MTTS
/// bit vector -- and bit 64 of MTTS means "a screen reader is in use". A server that honours
/// it turns off the maps and the ASCII art of its own accord, without anyone having to find
/// the setting.
/// </summary>
public sealed class TelnetProtocol
{
    // Commands.
    private const byte Iac = 255;
    private const byte Dont = 254;
    private const byte Do = 253;
    private const byte Wont = 252;
    private const byte Will = 251;
    private const byte Sb = 250;
    private const byte Se = 240;

    // Options.
    private const byte OptEcho = 1;
    private const byte OptSuppressGoAhead = 3;
    private const byte OptTerminalType = 24;
    private const byte OptEndOfRecord = 25;
    private const byte OptWindowSize = 31;
    private const byte OptCharset = 42;
    private const byte OptServerStatus = 70;
    private const byte OptMudSound = 90;
    private const byte OptGmcp = 201;

    // Subnegotiation verbs for TERMINAL-TYPE.
    private const byte TerminalTypeIs = 0;
    private const byte TerminalTypeSend = 1;

    // Subnegotiation verbs for CHARSET (RFC 2066).
    private const byte CharsetRequest = 1;
    private const byte CharsetAccepted = 2;
    private const byte CharsetRejected = 3;

    /// <summary>
    /// ANSI (1) + UTF-8 (4) + 256 colours (8) + SCREEN READER (64). The first three describe
    /// what the VT engine behind this actually understands. The fourth is the one that matters:
    /// it is how a MUD is told to leave out the room maps and the ASCII art.
    /// </summary>
    public const string MttsAnswer = "MTTS 77";

    private enum State { Data, Command, Will, Wont, Do, Dont, Subnegotiation, SubnegotiationIac }

    /// <summary>Options this end has agreed to perform.</summary>
    private readonly HashSet<byte> _localOn = new();

    /// <summary>Options the other end has been told it may perform.</summary>
    private readonly HashSet<byte> _remoteOn = new();

    private readonly List<byte> _subnegotiation = new();
    private readonly List<string> _soundRequests = new();
    private readonly List<GmcpMessage> _gmcp = new();
    private readonly Dictionary<string, string> _serverStatus =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly string _clientName;

    private State _state = State.Data;
    private int _terminalTypeAsked;
    private bool _lastWasCarriageReturn;
    private bool _pendingWindowSize;

    public TelnetProtocol(string clientName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        _clientName = clientName.ToUpperInvariant();
    }

    /// <summary>Whether the far end asked for the window size, so changes are worth sending.</summary>
    public bool WindowSizeAgreed => _localOn.Contains(OptWindowSize);

    /// <summary>
    /// Splits received bytes into the text the terminal should see and the protocol reply owed
    /// back. <paramref name="text"/> must be at least as long as <paramref name="received"/>;
    /// this only ever removes bytes.
    /// </summary>
    /// <returns>How many bytes of <paramref name="text"/> were written.</returns>
    public int Receive(ReadOnlySpan<byte> received, Span<byte> text, List<byte> reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        if (text.Length < received.Length)
            throw new ArgumentException("The text buffer must be at least as long as the input.",
                                        nameof(text));

        int written = 0;
        foreach (byte value in received)
        {
            switch (_state)
            {
                case State.Data:
                    if (value == Iac) { _state = State.Command; break; }
                    // RFC 854: a carriage return that means only "return" is sent as CR NUL.
                    // The NUL is padding, and a terminal that prints it prints nothing useful.
                    if (value == 0 && _lastWasCarriageReturn) break;
                    _lastWasCarriageReturn = value == (byte)'\r';
                    text[written++] = value;
                    break;

                case State.Command:
                    _state = value switch
                    {
                        Will => State.Will,
                        Wont => State.Wont,
                        Do => State.Do,
                        Dont => State.Dont,
                        Sb => State.Subnegotiation,
                        // IAC IAC is a literal 255. Every other command -- Go Ahead, End Of
                        // Record, No Operation, the interrupt verbs -- carries no text and is
                        // complete in two bytes, so simply dropping it is the whole handling.
                        _ => State.Data,
                    };
                    if (value == Iac) { _lastWasCarriageReturn = false; text[written++] = Iac; }
                    if (_state == State.Subnegotiation) _subnegotiation.Clear();
                    break;

                case State.Will: AnswerWill(value, reply); _state = State.Data; break;
                case State.Wont: AnswerWont(value, reply); _state = State.Data; break;
                case State.Do: AnswerDo(value, reply); _state = State.Data; break;
                case State.Dont: AnswerDont(value, reply); _state = State.Data; break;

                case State.Subnegotiation:
                    if (value == Iac) _state = State.SubnegotiationIac;
                    else _subnegotiation.Add(value);
                    break;

                case State.SubnegotiationIac:
                    if (value == Se)
                    {
                        Subnegotiated(reply);
                        _state = State.Data;
                    }
                    else
                    {
                        // IAC IAC inside a subnegotiation is a literal 255; anything else is a
                        // command the sender had no business putting here, and is ignored.
                        if (value == Iac) _subnegotiation.Add(Iac);
                        _state = State.Subnegotiation;
                    }
                    break;
            }
        }

        return written;
    }

    /// <summary>
    /// The window size, in the form the far end expects. Sent when it has been agreed and the
    /// terminal is resized, so a MUD wraps its own text to the width being read.
    /// </summary>
    public static void AppendWindowSize(List<byte> reply, int columns, int rows)
    {
        ArgumentNullException.ThrowIfNull(reply);
        TerminalSize size = TerminalSize.Validate(columns, rows);
        reply.Add(Iac);
        reply.Add(Sb);
        reply.Add(OptWindowSize);
        AppendEscaped(reply, (byte)(size.Columns >> 8));
        AppendEscaped(reply, (byte)(size.Columns & 0xFF));
        AppendEscaped(reply, (byte)(size.Rows >> 8));
        AppendEscaped(reply, (byte)(size.Rows & 0xFF));
        reply.Add(Iac);
        reply.Add(Se);
    }

    /// <summary>
    /// Typed bytes, ready for the wire. A byte that happens to be 255 has to be doubled or the
    /// far end reads it as the start of a command.
    /// </summary>
    public static byte[] Escape(ReadOnlySpan<byte> typed)
    {
        int extra = 0;
        foreach (byte value in typed) if (value == Iac) extra++;
        if (extra == 0) return typed.ToArray();

        var escaped = new byte[typed.Length + extra];
        int at = 0;
        foreach (byte value in typed)
        {
            escaped[at++] = value;
            if (value == Iac) escaped[at++] = Iac;
        }
        return escaped;
    }

    // ---- Answering ----

    /// <summary>Options this end performs. Everything absent from this list is declined.</summary>
    private static bool WeCanPerform(byte option)
        => option is OptTerminalType or OptWindowSize or OptSuppressGoAhead;

    /// <summary>
    /// Options the far end may perform. Echo belongs here because a password prompt turns it
    /// off, and Suppress Go Ahead because character-at-a-time is what every MUD expects.
    /// </summary>
    private static bool TheyMayPerform(byte option)
        => option is OptEcho or OptSuppressGoAhead or OptEndOfRecord or OptCharset or OptMudSound
                  or OptGmcp or OptServerStatus;

    /// <summary>Whether the far end has been told it may send MUD Sound Protocol triggers.</summary>
    public bool MudSoundAgreed => _remoteOn.Contains(OptMudSound);

    private void AnswerWill(byte option, List<byte> reply)
    {
        if (!TheyMayPerform(option)) { Send(Dont, option, reply); return; }
        // Silence when nothing changes is what keeps two polite ends from answering each
        // other forever.
        if (!_remoteOn.Add(option)) return;
        Send(Do, option, reply);
        if (option == OptGmcp) IntroduceOverGmcp(reply);
    }

    /// <summary>
    /// Says who is asking, and what it wants to hear about.
    ///
    /// GMCP is a subscription: a MUD sends the packages the client asked for and nothing else.
    /// Saying nothing after agreeing therefore agrees to receive nothing, which is how a client
    /// ends up negotiating a protocol it never sees a message from. The packages named here
    /// are the ones with something to tell a player who cannot see the screen: where they are,
    /// what the exits are, and how the character is doing.
    /// </summary>
    private void IntroduceOverGmcp(List<byte> reply)
    {
        SendGmcp($"Core.Hello {{ \"client\": \"{_clientName}\", \"version\": \"{VersionInfo.Current}\" }}",
                 reply);
        SendGmcp(
            """Core.Supports.Set [ "Char 1", "Char.Vitals 1", "Char.Status 1", "Room 1", "Comm.Channel 1" ]""",
            reply);
    }

    private static void SendGmcp(string message, List<byte> reply)
    {
        reply.Add(Iac);
        reply.Add(Sb);
        reply.Add(OptGmcp);
        foreach (byte value in Encoding.UTF8.GetBytes(message)) AppendEscaped(reply, value);
        reply.Add(Iac);
        reply.Add(Se);
    }

    private void AnswerWont(byte option, List<byte> reply)
    {
        if (_remoteOn.Remove(option) || !TheyMayPerform(option)) Send(Dont, option, reply);
    }

    private void AnswerDo(byte option, List<byte> reply)
    {
        if (!WeCanPerform(option)) { Send(Wont, option, reply); return; }
        // Silence when nothing changes. The agreement still stands, which is why the window
        // size below is owed either way -- a server that is answering an offer this end made
        // first would otherwise never be told the size it just asked for.
        if (_localOn.Add(option)) Send(Will, option, reply);
        if (option == OptWindowSize) _pendingWindowSize = true;
    }

    private void AnswerDont(byte option, List<byte> reply)
    {
        if (_localOn.Remove(option) || !WeCanPerform(option)) Send(Wont, option, reply);
    }

    /// <summary>
    /// Whether the window size has just been agreed and has not been sent yet. The session
    /// knows the size; this class only knows that it is owed.
    /// </summary>
    public bool TakeWindowSizeRequest()
    {
        bool owed = _pendingWindowSize;
        _pendingWindowSize = false;
        return owed;
    }

    private static void Send(byte command, byte option, List<byte> reply)
    {
        reply.Add(Iac);
        reply.Add(command);
        reply.Add(option);
    }

    private static void AppendEscaped(List<byte> reply, byte value)
    {
        reply.Add(value);
        if (value == Iac) reply.Add(Iac);
    }

    /// <summary>
    /// Hands over any sound requests that arrived out of band, and forgets them.
    ///
    /// A MUD may put its triggers in the text, where <see cref="MspScanner"/> finds them, or
    /// send them inside a subnegotiation so that a client which does not speak the protocol
    /// never sees them at all. Core MUD does the second. Both have to work.
    /// </summary>
    public void DrainMudSoundRequests(List<string> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        if (_soundRequests.Count == 0) return;
        into.AddRange(_soundRequests);
        _soundRequests.Clear();
    }

    /// <summary>Whether the far end has been told it may send GMCP.</summary>
    public bool GmcpAgreed => _remoteOn.Contains(OptGmcp);

    /// <summary>Hands over the GMCP messages that have arrived, and forgets them.</summary>
    public void DrainGmcp(List<GmcpMessage> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        if (_gmcp.Count == 0) return;
        into.AddRange(_gmcp);
        _gmcp.Clear();
    }

    /// <summary>
    /// Sends a message to the far end's GMCP handler. Nothing is sent when it never agreed to
    /// the protocol: a subnegotiation for an option that was refused is a protocol error.
    /// </summary>
    public bool TrySendGmcp(string message, List<byte> reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!GmcpAgreed) return false;
        SendGmcp(message, reply);
        return true;
    }

    /// <summary>
    /// What the server said about itself when asked, as MUD Server Status Protocol variables.
    ///
    /// Read-only and purely informational -- the name of the MUD, how long it has been up, how
    /// many rooms it has. It is here because a server that offers it is offering the one
    /// description of itself that does not have to be found in a banner.
    /// </summary>
    public IReadOnlyDictionary<string, string> ServerStatus => _serverStatus;

    // MSSP packs its variables as VAR name VAL value, repeated.
    private const byte MsspVariable = 1;
    private const byte MsspValue = 2;

    private void ReadServerStatus()
    {
        string? name = null;
        var text = new List<byte>();
        bool inValue = false;

        string Take()
        {
            string value = Encoding.UTF8.GetString([.. text]).Trim();
            text.Clear();
            return value;
        }

        void Close()
        {
            string value = Take();
            if (name is not { Length: > 0 }) return;
            // A server may repeat a variable to give it several values.
            _serverStatus[name] = _serverStatus.TryGetValue(name, out string? had) && had.Length > 0
                ? $"{had}, {value}"
                : value;
        }

        for (int i = 1; i < _subnegotiation.Count; i++)
        {
            switch (_subnegotiation[i])
            {
                case MsspVariable:
                    if (inValue) Close();
                    name = null;
                    text.Clear();
                    inValue = false;
                    break;
                case MsspValue:
                    name = Take();
                    inValue = true;
                    break;
                default:
                    text.Add(_subnegotiation[i]);
                    break;
            }
        }
        if (inValue) Close();
    }

    private void Subnegotiated(List<byte> reply)
    {
        if (_subnegotiation.Count >= 3 &&
            _subnegotiation[0] == OptCharset &&
            _subnegotiation[1] == CharsetRequest)
        {
            AnswerCharset(reply);
            _subnegotiation.Clear();
            return;
        }

        if (_subnegotiation.Count >= 2 && _subnegotiation[0] == OptMudSound)
        {
            _soundRequests.Add(Encoding.UTF8.GetString([.. _subnegotiation[1..]]));
            _subnegotiation.Clear();
            return;
        }

        if (_subnegotiation.Count >= 2 && _subnegotiation[0] == OptGmcp)
        {
            if (GmcpMessage.TryParse(Encoding.UTF8.GetString([.. _subnegotiation[1..]]),
                                     out GmcpMessage? message))
            {
                _gmcp.Add(message);
            }
            _subnegotiation.Clear();
            return;
        }

        if (_subnegotiation.Count >= 2 && _subnegotiation[0] == OptServerStatus)
        {
            ReadServerStatus();
            _subnegotiation.Clear();
            return;
        }

        if (_subnegotiation.Count >= 2 &&
            _subnegotiation[0] == OptTerminalType &&
            _subnegotiation[1] == TerminalTypeSend)
        {
            reply.Add(Iac);
            reply.Add(Sb);
            reply.Add(OptTerminalType);
            reply.Add(TerminalTypeIs);
            foreach (char c in TerminalTypeAnswer()) AppendEscaped(reply, (byte)c);
            reply.Add(Iac);
            reply.Add(Se);
        }

        _subnegotiation.Clear();
    }

    /// <summary>
    /// Selects UTF-8 when it appears in a CHARSET request. The VT engine consumes UTF-8, and
    /// MTTS advertises that fact, so declining Core MUD's matching telnet offer leaves the two
    /// ends needlessly guessing about every non-ASCII character.
    /// </summary>
    private void AnswerCharset(List<byte> reply)
    {
        byte separator = _subnegotiation[2];
        string offered = Encoding.ASCII.GetString([.. _subnegotiation[3..]]);
        bool utf8 = offered.Split((char)separator, StringSplitOptions.RemoveEmptyEntries)
            .Any(value => value.Trim().Equals("UTF-8", StringComparison.OrdinalIgnoreCase)
                       || value.Trim().Equals("UTF8", StringComparison.OrdinalIgnoreCase));

        reply.Add(Iac);
        reply.Add(Sb);
        reply.Add(OptCharset);
        reply.Add(utf8 ? CharsetAccepted : CharsetRejected);
        if (utf8)
        {
            foreach (byte value in "UTF-8"u8) AppendEscaped(reply, value);
        }
        reply.Add(Iac);
        reply.Add(Se);
    }

    /// <summary>
    /// The MUD terminal-type cycle: the client's name, then the terminal it emulates, then
    /// what it can do. A server keeps asking until an answer repeats, so the last one stays.
    /// </summary>
    private string TerminalTypeAnswer()
    {
        string answer = _terminalTypeAsked switch
        {
            0 => _clientName,
            1 => "ANSI",
            _ => MttsAnswer,
        };
        if (_terminalTypeAsked < 2) _terminalTypeAsked++;
        return answer;
    }
}
