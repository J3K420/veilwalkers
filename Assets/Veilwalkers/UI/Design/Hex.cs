using UnityEngine;

namespace Veilwalkers.UI
{
    /// <summary>
    /// Deterministic <c>#RRGGBB</c> → opaque <see cref="Color32"/> parser for the Pumpkin Patch
    /// design tokens (Story 6.1). Hand-rolled (not <c>UnityEngine.ColorUtility.TryParseHtmlString</c>)
    /// so the conversion is trivial, allocation-free, and identical headless — the canonical
    /// <c>#RRGGBB</c> string stays the auditable source of truth, this turns it into the runtime
    /// <see cref="Color32"/>. Alpha is ALWAYS 255 (every token is fully opaque).
    /// </summary>
    internal static class Hex
    {
        /// <summary>
        /// Parse a <c>#RRGGBB</c> string (leading <c>#</c> required, exactly 6 hex digits) to an
        /// opaque <see cref="Color32"/>. The tokens are compile-time constants authored by hand, so
        /// a malformed literal is a programmer error — this throws rather than silently degrading
        /// (the tokens are pinned by tests, so a typo fails loudly at test time).
        /// </summary>
        internal static Color32 ToColor32(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length != 7 || hex[0] != '#')
            {
                throw new System.ArgumentException($"Expected a #RRGGBB hex string, got '{hex}'.", nameof(hex));
            }

            byte r = (byte)((Nibble(hex[1]) << 4) | Nibble(hex[2]));
            byte g = (byte)((Nibble(hex[3]) << 4) | Nibble(hex[4]));
            byte b = (byte)((Nibble(hex[5]) << 4) | Nibble(hex[6]));
            return new Color32(r, g, b, 255);
        }

        /// <summary>
        /// The packed <c>0xRRGGBBAA</c> integer for a token's <see cref="Color32"/>. Used by tests
        /// (and the AC-4 containment guard) for a clean value comparison that sidesteps the
        /// <see cref="Color32"/> struct-equality trap.
        /// </summary>
        internal static int Packed(Color32 c) => (c.r << 24) | (c.g << 16) | (c.b << 8) | c.a;

        private static int Nibble(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            throw new System.ArgumentException($"Invalid hex digit '{c}'.");
        }
    }
}
