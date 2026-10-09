using System.Diagnostics.CodeAnalysis;
using Plus.Utilities;

namespace Plus.Communication.Encryption.KeyExchange;

public class DiffieHellman
{
    public readonly int Bitlength = 32;

    private BigInteger _privateKey;

    public DiffieHellman() : this(32) { }

    public DiffieHellman(int b)
    {
        Bitlength = b;
        Prime = BigInteger.genPseudoPrime(Bitlength, 10, Random.Shared);
        Generator = BigInteger.genPseudoPrime(Bitlength, 10, Random.Shared);
        GenerateKeys(false);
    }

    public DiffieHellman(BigInteger prime, BigInteger generator)
    {
        Prime = prime;
        Generator = generator;
        GenerateKeys(true);
    }

    public BigInteger Prime { get; private set; }
    public BigInteger Generator { get; private set; }
    public BigInteger PublicKey { get; private set; }

    [MemberNotNull(nameof(_privateKey), nameof(PublicKey))]
    private void GenerateKeys(bool requireNonZeroPublicKey)
    {
        if (Generator > Prime) {
            (Prime, Generator) = (Generator, Prime);
        }

        do {
            var bytes = new byte[Bitlength / 8];
            Randomizer.NextBytes(bytes);
            _privateKey = new(bytes);
            PublicKey = Generator.modPow(_privateKey, Prime);
        } while (requireNonZeroPublicKey && PublicKey == 0);
    }

    public BigInteger CalculateSharedKey(BigInteger m) => m.modPow(_privateKey, Prime);
}
