namespace Plus.Communication.Encryption.Crypto.Prng;

public class Arc4
{
    public const int Poolsize = 256;
    private readonly byte[] _bytes;
    private int _i;
    private int _j;

    public Arc4()
    {
        _bytes = new byte[Poolsize];
    }

    public Arc4(byte[] key)
    {
        _bytes = new byte[Poolsize];
        Initialize(key);
    }

    public void Initialize(byte[] key)
    {
        _i = 0;
        _j = 0;

        for (_i = 0; _i < Poolsize; ++_i) {
            _bytes[_i] = (byte)_i;
        }

        for (_i = 0; _i < Poolsize; ++_i) {
            _j = (_j + _bytes[_i] + key[_i % key.Length]) & (Poolsize - 1);
            Swap(_i, _j);
        }

        _i = 0;
        _j = 0;
    }

    private void Swap(int a, int b)
    {
        var t = _bytes[a];
        _bytes[a] = _bytes[b];
        _bytes[b] = t;
    }

    public byte Next()
    {
        _i = ++_i & (Poolsize - 1);
        _j = (_j + _bytes[_i]) & (Poolsize - 1);
        Swap(_i, _j);

        return _bytes[(_bytes[_i] + _bytes[_j]) & 255];
    }

    public void Encrypt(ref byte[] src)
    {
        Transform(src);
    }

    public void Decrypt(ref byte[] src)
    {
        Transform(src);
    }

    public void Transform(Span<byte> source)
    {
        for (var k = 0; k < source.Length; k++) {
            source[k] ^= Next();
        }
    }
}
