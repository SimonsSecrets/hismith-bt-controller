namespace HismithController.Services;

// Redaction helpers for BLE identifiers in log output. A device's address and advertised name
// can both be PII (a stable per-device id; a user-personalized name), so they are masked before
// reaching the on-disk logs while leaving enough to diagnose with (OpenPoints.md §4).
internal static class BleLog
{
    // Masks all but the final octet of a formatted address (XX:XX:XX:XX:XX:XX). The first three
    // octets are the manufacturer OUI; the whole value identifies a specific device. The last
    // octet is retained so several devices seen in one scan — and a later connect to one of them
    // — can still be told apart / correlated.
    public static string RedactAddress(string formatted)
    {
        // FormatAddress always emits six colon-separated octets; mask everything before the last.
        int lastColon = formatted.LastIndexOf(':');
        return lastColon < 0
            ? "**"
            : $"**:**:**:**:**:{formatted[(lastColon + 1)..]}";
    }

    // Masks an advertised name. Most devices broadcast a generic model name (e.g. "HISMITH"),
    // but some peripherals use a user-personalized one. Keep only the first character so the
    // model family stays recognizable, then a fixed "***" — length is not preserved, so the
    // masked form leaks nothing beyond the leading character.
    public static string RedactName(string? name)
        => string.IsNullOrEmpty(name) ? "(unknown)" : $"{name[0]}***";
}
