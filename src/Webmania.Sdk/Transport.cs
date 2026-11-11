using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Runtime.InteropServices;
using System.Text;

namespace Webmania;

// Owns connection setup independently of SocketsHttpHandler's detached connection queue.
// The handler remains the only HTTP engine; this object observes metadata and framing only.
internal sealed class TransferContext(CancellationToken token)
{
    internal static readonly HttpRequestOptionsKey<TransferContext> Key = new("Webmania.Transfer");
    private readonly object gate = new();
    private readonly List<Task> setup = [];
    private bool closed;
    internal bool Sent { get; set; }
    internal bool Observed { get; set; }
    internal bool ObservationComplete { get; set; }
    internal bool UnsafeObservation { get; set; }
    internal readonly List<string> Locations = [];
    internal CancellationToken Token => token;
    internal Task<Stream> Start(Func<Task<Stream>> factory)
    {
        lock (gate)
        {
            if (closed || token.IsCancellationRequested) return Task.FromCanceled<Stream>(new CancellationToken(true));
            var task = factory(); setup.Add(task); return task;
        }
    }
    internal async Task ReapAsync()
    {
        Task[] tasks;
        lock (gate) { closed = true; tasks = setup.ToArray(); }
        try { await Task.WhenAll(tasks).ConfigureAwait(false); } catch { /* public transfer maps the failure */ }
    }
}

internal static class OwnedConnection
{
    internal static ValueTask<Stream> Connect(SocketsHttpConnectionContext context, CancellationToken runtimeToken, RemoteCertificateValidationCallback? validate, string? helperHost = null)
    {
        if (!context.InitialRequestMessage.Options.TryGetValue(TransferContext.Key, out var request)) throw new IOException("Missing request context.");
        return new(request.Start(async () =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(request.Token, runtimeToken);
            var token = linked.Token;
            var addresses = await ResolveAsync(context.DnsEndPoint.Host, token, helperHost).ConfigureAwait(false);
            Socket? socket = null;
            try
            {
                // An owned socket and cancellation registration cover SYN and TLS as well as DNS.
                socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                using var cancel = token.UnsafeRegister(static s => ((Socket)s!).Dispose(), socket);
                await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, token).ConfigureAwait(false);
                Stream stream = new NetworkStream(socket, ownsSocket: true);
                if (context.InitialRequestMessage.RequestUri!.Scheme == "https")
                {
                    var ssl = new SslStream(stream, false, validate);
                    try
                    {
                        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                        {
                            TargetHost = context.DnsEndPoint.Host,
                            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                            EncryptionPolicy = EncryptionPolicy.RequireEncryption,
                            ApplicationProtocols = [SslApplicationProtocol.Http11]
                        }, token).ConfigureAwait(false);
                        if (!ssl.IsAuthenticated || !ssl.IsEncrypted || !ssl.IsSigned) throw new AuthenticationException("Authenticated encrypted TLS is required.");
                        stream = ssl;
                    }
                    catch { ssl.Dispose(); throw; }
                }
                token.ThrowIfCancellationRequested();
                return stream;
            }
            catch { socket?.Dispose(); token.ThrowIfCancellationRequested(); throw; }
        }));
    }
    /// <summary>Default .NET host next to the running shared runtime, or null when the app is self-contained.</summary>
    internal static string? DefaultHelperHost()
    {
        var root = Directory.GetParent(RuntimeEnvironment.GetRuntimeDirectory().TrimEnd(Path.DirectorySeparatorChar))?.Parent?.Parent?.FullName;
        var host = root is null ? null : Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        return host is not null && File.Exists(host) ? host : null;
    }
    private static async Task<IPAddress[]> ResolveAsync(string host, CancellationToken token, string? helperHost = null)
    {
        token.ThrowIfCancellationRequested();
        if (IPAddress.TryParse(host, out var literal)) return [literal];
        var dir = Path.Combine(Path.GetTempPath(), "webmania-dns-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(dir);
        else Directory.CreateDirectory(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            foreach (var name in new[] { "Webmania.DnsHelper.dll", "Webmania.DnsHelper.runtimeconfig.json" })
            {
                using var resource = typeof(OwnedConnection).Assembly.GetManifestResourceStream("Webmania.Dns." + name) ?? throw new IOException("Packaged resolver is missing.");
                using var output = File.Create(Path.Combine(dir, name)); resource.CopyTo(output);
            }
            // A self-contained app has no host next to its runtime: WebmaniaClientOptions.DnsHelperHost names one.
            var executable = helperHost ?? DefaultHelperHost() ?? throw new IOException("No .NET host is available for the name resolver; set WebmaniaClientOptions.DnsHelperHost.");
            var start = new ProcessStartInfo(executable) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            // Do not propagate caller credential variables, startup hooks or secrets.
            start.Environment.Clear();
            if (OperatingSystem.IsWindows()) start.Environment["SystemRoot"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
            start.ArgumentList.Add(Path.Combine(dir, "Webmania.DnsHelper.dll"));
            using var process = new Process { StartInfo = start };
            token.ThrowIfCancellationRequested();
            if (!process.Start()) throw new IOException("Resolver did not start.");
            using var cancel = token.UnsafeRegister(static state => { try { var p = (Process)state!; if (!p.HasExited) p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }, process);
            try
            {
                await process.StandardInput.WriteLineAsync(host.AsMemory(), token).ConfigureAwait(false);
                process.StandardInput.Close();
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                var output = await outputTask.ConfigureAwait(false); await errorTask.ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (process.ExitCode != 0 || output.Length > 8192) throw new IOException("Name resolution failed.");
                var addresses = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(IPAddress.Parse).Take(64).ToArray();
                if (addresses.Length == 0) throw new IOException("Name resolution returned no address.");
                return addresses;
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}

// This bounded tap never supplies status, body, schemas or response objects to the SDK.
// SocketsHttpHandler still parses/validates HTTP. The tap only observes raw Location
// across 1xx/final/trailers and refuses ambiguous CL+TE before bytes reach that parser.
internal sealed class ObservedHttpStream(Stream inner, Func<TransferContext?> current) : Stream
{
    private TransferContext? owner;
    private readonly List<byte> line = [];
    private int state, headerBytes, informationals;
    private long remaining;
    private bool chunked, contentLength;
    private long? length;
    private int status;
    private void Bind()
    {
        var context = current() ?? throw new IOException("Missing active transfer.");
        context.Token.ThrowIfCancellationRequested();
        if (owner == context) return;
        owner = context; line.Clear(); state = 0; headerBytes = 0; informationals = 0; chunked = false; contentLength = false; length = null;
    }
    private void Observe(ReadOnlySpan<byte> bytes)
    {
        var c = owner; if (c is null) return;
        lock (c) ObserveLocked(bytes, c);
    }
    private void ObserveLocked(ReadOnlySpan<byte> bytes, TransferContext c)
    {
        if (bytes.Length > 0) c.Observed = true;
        foreach (var value in bytes)
        {
            if (state == 4) continue; // content-length or EOF body cannot contain trailers
            if (state == 3) { if (--remaining == 0) state = 5; continue; }
            if (state is 5 or 6)
            {
                if (value != (state == 5 ? 13 : 10)) Reject();
                state = state == 5 ? 6 : 2; continue;
            }
            if (++headerBytes > 65536 || line.Count >= 16384) Reject();
            line.Add(value);
            if (value != 10) continue;
            if (line.Count < 2 || line[^2] != 13) Reject();
            var text = Encoding.Latin1.GetString(line.ToArray(), 0, line.Count - 2); line.Clear();
            if (state == 0)
            {
                var parts = text.Split(' ', 3);
                if (parts.Length < 2 || !parts[0].StartsWith("HTTP/1.", StringComparison.Ordinal) || !int.TryParse(parts[1], out status)) Reject();
                chunked = false; contentLength = false; length = null; state = 1;
            }
            else if (state is 1 or 7)
            {
                if (text.Length == 0)
                {
                    if (state == 7) { c.ObservationComplete = !c.UnsafeObservation; state = 4; continue; }
                    if (status is >= 100 and < 200) { if (++informationals > 100 || status == 101) Reject(); state = 0; continue; }
                    if (chunked && contentLength) Reject();
                    if (chunked) { c.ObservationComplete = false; state = 2; }
                    else { c.ObservationComplete = !c.UnsafeObservation; state = 4; }
                    continue;
                }
                var colon = text.IndexOf(':');
                if (colon <= 0 || text[0] is ' ' or '\t') Reject();
                var name = text[..colon]; var field = text[(colon + 1)..].Trim(' ', '\t');
                if (name.Any(c => !char.IsAsciiLetterOrDigit(c) && !"!#$%&'*+-.^_`|~".Contains(c)) || field.Any(c => c < 32 && c != '\t' || c == 127)) Reject();
                if (state == 7 && (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) || name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))) Reject();
                if (name.Equals("Location", StringComparison.OrdinalIgnoreCase)) c.Locations.Add(field);
                if (state == 7) continue;
                if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) chunked = true;
                if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    if (!long.TryParse(field, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) || n < 0 || length.HasValue && length != n) Reject();
                    length = n; contentLength = true;
                }
                if (chunked && contentLength) Reject();
            }
            else if (state == 2)
            {
                var semicolon = text.IndexOf(';'); var number = semicolon < 0 ? text : text[..semicolon];
                if (!long.TryParse(number, System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out remaining) || remaining < 0) Reject();
                state = remaining == 0 ? 7 : 3;
            }
        }
    }
    private void Reject() { if (owner is { } c) { c.UnsafeObservation = true; c.ObservationComplete = false; } throw new IOException("HTTP metadata observation was rejected."); }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    { var n = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false); Observe(buffer.Span[..n]); return n; }
    public override int Read(byte[] buffer, int offset, int count) { var n = inner.Read(buffer, offset, count); Observe(buffer.AsSpan(offset, n)); return n; }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    { Bind(); if (buffer.Length > 0) owner!.Sent = true; await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false); }
    public override void Write(byte[] buffer, int offset, int count) { Bind(); if (count > 0) owner!.Sent = true; inner.Write(buffer, offset, count); }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset,count), cancellationToken).AsTask();
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => WriteAsync(buffer.AsMemory(offset,count), cancellationToken).AsTask();
    public override bool CanRead => inner.CanRead; public override bool CanWrite => inner.CanWrite; public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => inner.Flush(); public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    public override ValueTask DisposeAsync() => inner.DisposeAsync();
}
