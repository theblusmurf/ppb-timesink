namespace PoteMemoryProbe;

// The standalone probe retains its historical read approvals. The hunter adds
// a session approval after signature discovery succeeds for the exact file.
// Input separately requires completed validation of the connected live layout.
internal static class ClientCompatibility
{
    static readonly HashSet<string> ReadBuilds = new(StringComparer.OrdinalIgnoreCase)
    {
        "08b039fcc0930fabff4004afac611a66e58067c7c76405953791a22800d7fb53", // Patch 45
        "510cd56cfee8b853ccd4da8defca924206120c5977509b43f11158aa318d5871", // Patch 46
        "6a86726e72c98a9e68a3b4ee0905736d3803beaeac6fb7a70761b3ecf422ca09" // Patch 44
    };

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string,byte> discovered = new(StringComparer.OrdinalIgnoreCase);
    internal static bool SupportsRead(string hash) => ReadBuilds.Contains(hash) || discovered.ContainsKey(hash);
    internal static void AuthorizeDiscoveredRead(string hash)
    {
        if(hash.Length!=64 || !hash.All(Uri.IsHexDigit))throw new ArgumentException("A complete SHA-256 fingerprint is required.",nameof(hash));
        discovered.TryAdd(hash,0);
    }
}
