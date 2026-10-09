using System.Text;
using Plus.Communication.Encryption.Crypto.RSA;
using Plus.Communication.Encryption.KeyExchange;
using Plus.Communication.Encryption.Keys;
using Plus.Utilities;

namespace Plus.Communication.Encryption;

public static class HabboEncryptionV2
{
    private static RsaKey? _rsa;
    private static DiffieHellman? _diffieHellman;
    private static RsaKey Rsa => _rsa ?? throw new InvalidOperationException("Encryption has not been initialized.");
    private static DiffieHellman DiffieHellman => _diffieHellman ?? throw new InvalidOperationException("Encryption has not been initialized.");

    public static void Initialize(RsaKeys keys)
    {
        _rsa = RsaKey.ParsePrivateKey(keys.N, keys.E, keys.D);
        _diffieHellman = new();
    }

    private static string GetRsaStringEncrypted(string message)
    {
        try {
            var m = Encoding.Default.GetBytes(message);
            var c = Rsa.Sign(m);

            return c == null ? "0" : Converter.BytesToHexString(c);
        }
        catch {
            return "0";
        }
    }

    public static string GetRsaDiffieHellmanPrimeKey()
    {
        var key = DiffieHellman.Prime.ToString(10);

        return GetRsaStringEncrypted(key);
    }

    public static string GetRsaDiffieHellmanGeneratorKey()
    {
        var key = DiffieHellman.Generator.ToString(10);

        return GetRsaStringEncrypted(key);
    }

    public static string GetRsaDiffieHellmanPublicKey()
    {
        var key = DiffieHellman.PublicKey.ToString(10);

        return GetRsaStringEncrypted(key);
    }

    public static BigInteger CalculateDiffieHellmanSharedKey(string publicKey)
    {
        try {
            var cbytes = Converter.HexStringToBytes(publicKey);
            var publicKeyBytes = Rsa.Verify(cbytes);

            if (publicKeyBytes == null) {
                return 0;
            }

            var publicKeyString = Encoding.Default.GetString(publicKeyBytes);

            return DiffieHellman.CalculateSharedKey(new(publicKeyString, 10));
        }
        catch {
            return 0;
        }
    }
}
