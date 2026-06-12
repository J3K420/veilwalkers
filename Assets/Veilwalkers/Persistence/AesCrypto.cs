using System;
using System.Security.Cryptography;
using System.Text;

namespace Veilwalkers.Persistence
{
    /// <summary>
    /// AES-CBC encryption of the save payload with an encrypt-then-MAC integrity
    /// check, so any byte-level tampering deterministically throws
    /// <see cref="SaveCorruptException"/> on load (AC-3). Bare CBC alone only throws
    /// on (some) padding corruption; the HMAC is what makes the AR-20 tamper test
    /// deterministic. File layout: <c>IV (16) ‖ ciphertext ‖ HMAC-SHA256 (32)</c>,
    /// where the MAC covers <c>IV ‖ ciphertext</c> — covering the IV matters because
    /// an IV-only flip would otherwise corrupt just the first plaintext block and
    /// detection would hinge on JSON-parse luck.
    /// <para>
    /// Keys derive deterministically from an embedded app secret (derived once,
    /// cached). This raises the bar against casual save-editing ONLY — the
    /// architecture explicitly accepts it is not cheat-proof, since the secret ships
    /// in the client. AES-GCM is not reliably available on Unity's IL2CPP/Android
    /// profile; CBC + HMAC is the portable equivalent. All members are safe to call
    /// from any thread (crypto primitives are created per call).
    /// </para>
    /// </summary>
    public static class AesCrypto
    {
        private const int IvSizeBytes = 16;
        private const int MacSizeBytes = 32;
        private const int KeySizeBytes = 32;

        // Embedded app secret + fixed salt: deterministic on every install so saves
        // survive device transfers (deviceUniqueIdentifier would brick them, and is
        // main-thread-only anyway). Iteration count is modest on purpose — the secret
        // ships in the client, so more iterations buy no real security here.
        private const string EmbeddedSecret = "Veilwalkers.Save.v1.k3A9xQm5fTw2pZr8";
        private const int DeriveIterations = 10000;

        private static readonly byte[] Salt =
        {
            0x56, 0x65, 0x69, 0x6C, 0x77, 0x61, 0x6C, 0x6B,
            0x65, 0x72, 0x73, 0x2E, 0x76, 0x31, 0x21, 0x9D,
        };

        // Derive ONCE and cache: 64 bytes split into the AES key (first 32) and the
        // HMAC key (last 32). Lazy is thread-safe for concurrent first use.
        private static readonly Lazy<byte[]> KeyMaterial = new Lazy<byte[]>(DeriveKeyMaterial);

        /// <summary>
        /// Encrypt a UTF-8 JSON payload. Returns <c>IV ‖ ciphertext ‖ MAC</c> with a
        /// fresh random IV per call.
        /// </summary>
        public static byte[] Encrypt(byte[] plaintext)
        {
            if (plaintext == null)
            {
                throw new ArgumentNullException(nameof(plaintext));
            }

            using (var aes = Aes.Create())
            {
                aes.Key = AesKey();
                aes.GenerateIV();

                byte[] ciphertext;
                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                {
                    ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
                }

                var output = new byte[IvSizeBytes + ciphertext.Length + MacSizeBytes];
                Buffer.BlockCopy(aes.IV, 0, output, 0, IvSizeBytes);
                Buffer.BlockCopy(ciphertext, 0, output, IvSizeBytes, ciphertext.Length);

                byte[] mac = ComputeMac(output, IvSizeBytes + ciphertext.Length);
                Buffer.BlockCopy(mac, 0, output, IvSizeBytes + ciphertext.Length, MacSizeBytes);
                return output;
            }
        }

        /// <summary>
        /// Verify the MAC FIRST, then decrypt. Throws <see cref="SaveCorruptException"/>
        /// on any integrity or decrypt failure — truncation, byte flips anywhere in
        /// the blob (IV, ciphertext, or MAC), or padding corruption.
        /// </summary>
        public static byte[] Decrypt(byte[] blob)
        {
            if (blob == null)
            {
                throw new ArgumentNullException(nameof(blob));
            }

            // Minimum valid blob: IV + one AES block + MAC.
            if (blob.Length < IvSizeBytes + 16 + MacSizeBytes)
            {
                throw new SaveCorruptException(
                    $"Save blob is too short to be valid ({blob.Length} bytes).");
            }

            int macOffset = blob.Length - MacSizeBytes;
            byte[] expectedMac = ComputeMac(blob, macOffset);
            if (!FixedTimeEquals(blob, macOffset, expectedMac))
            {
                throw new SaveCorruptException(
                    "Save integrity check failed (MAC mismatch) — the file is corrupt or was tampered with.");
            }

            try
            {
                using (var aes = Aes.Create())
                {
                    aes.Key = AesKey();
                    var iv = new byte[IvSizeBytes];
                    Buffer.BlockCopy(blob, 0, iv, 0, IvSizeBytes);
                    aes.IV = iv;

                    using (ICryptoTransform decryptor = aes.CreateDecryptor())
                    {
                        return decryptor.TransformFinalBlock(
                            blob, IvSizeBytes, macOffset - IvSizeBytes);
                    }
                }
            }
            catch (CryptographicException ex)
            {
                // Unreachable in practice once the MAC has passed, but the contract
                // stands: corruption surfaces as the typed exception, never raw crypto.
                throw new SaveCorruptException("Save payload failed to decrypt.", ex);
            }
        }

        private static byte[] DeriveKeyMaterial()
        {
            using (var derive = new Rfc2898DeriveBytes(
                Encoding.UTF8.GetBytes(EmbeddedSecret), Salt, DeriveIterations, HashAlgorithmName.SHA256))
            {
                return derive.GetBytes(KeySizeBytes * 2);
            }
        }

        private static byte[] AesKey()
        {
            var key = new byte[KeySizeBytes];
            Buffer.BlockCopy(KeyMaterial.Value, 0, key, 0, KeySizeBytes);
            return key;
        }

        private static byte[] ComputeMac(byte[] buffer, int count)
        {
            var macKey = new byte[KeySizeBytes];
            Buffer.BlockCopy(KeyMaterial.Value, KeySizeBytes, macKey, 0, KeySizeBytes);

            using (var hmac = new HMACSHA256(macKey))
            {
                return hmac.ComputeHash(buffer, 0, count);
            }
        }

        /// <summary>
        /// Constant-time comparison of <paramref name="expected"/> against the bytes
        /// of <paramref name="buffer"/> starting at <paramref name="offset"/>, so the
        /// comparison itself leaks no early-exit timing.
        /// </summary>
        private static bool FixedTimeEquals(byte[] buffer, int offset, byte[] expected)
        {
            if (buffer.Length - offset != expected.Length)
            {
                return false;
            }

            int diff = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                diff |= buffer[offset + i] ^ expected[i];
            }

            return diff == 0;
        }
    }
}
