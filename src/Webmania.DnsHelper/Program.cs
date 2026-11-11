using System.Net;
// Request-owned native resolver boundary. Hostnames travel over stdin, never argv/env.
try
{
    var host = await Console.In.ReadLineAsync();
    if (string.IsNullOrWhiteSpace(host) || host.Length > 253 || host.Any(char.IsControl)) return 2;
    var addresses = await Dns.GetHostAddressesAsync(host);
    foreach (var address in addresses.Take(64)) Console.WriteLine(address.ToString());
    return addresses.Length == 0 ? 3 : 0;
}
catch { return 1; }
