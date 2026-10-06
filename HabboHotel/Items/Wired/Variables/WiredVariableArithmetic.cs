using System.Numerics;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>Active Octane/Polaris operator codes (Turbo's enum values are different).</summary>
public static class WiredVariableArithmetic
{
    public static bool IsSupported(int operation) => operation is >= 0 and <= 6 or 40 or 41 or 50 or 60
        or >= 100 and <= 105 or >= 110 and <= 122;
    public static bool IsUnary(int operation) => operation is 60 or 103 or 110;
    public static int Apply(int operation, int current, int operand, Random? random = null)
    {
        if (!IsSupported(operation)) {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        long result = operation switch
        {
            0 => operand,
            1 => (long)current + operand,
            2 => (long)current - operand,
            3 => (long)current * operand,
            4 => operand == 0 ? current : (long)current / operand,
            5 => Power(current, operand),
            6 => operand == 0 ? current : (long)current % operand,
            40 => Math.Min(current, operand),
            41 => Math.Max(current, operand),
            50 => operand <= 0 ? 0 : (random ?? Random.Shared).NextInt64((long)operand + 1),
            60 => Math.Abs((long)current),
            100 => current & operand,
            101 => current | operand,
            102 => current ^ operand,
            103 => ~current,
            104 => current << Math.Clamp(operand, 0, 31),
            105 => current >> Math.Clamp(operand, 0, 31),
            110 => BitOperations.PopCount((uint)current),
            115 => operand is >= 0 and < 32 ? (int)((uint)current >> operand & 1) : 0,
            116 => operand is >= 0 and < 32 ? current | (1 << operand) : current,
            117 => operand is >= 0 and < 32 ? current & ~(1 << operand) : current,
            118 => operand is >= 0 and < 32 ? current ^ (1 << operand) : current,
            _ => Scan(operation, current, operand)
        };

        return (int)Math.Clamp(result, int.MinValue, int.MaxValue);
    }
    private static long Power(int current, int operand)
    {
        if (operand < 0) {
            return 0;
        }

        var result = Math.Pow(current, operand);

        return (long)Math.Clamp(result, int.MinValue, int.MaxValue);
    }
    private static int Scan(int operation, int current, int operand)
    {
        var forward = operation is 111 or 112 or 119 or 120;
        var high = operation is 112 or 114 or 120 or 122;
        var start = (long)operand + (operation >= 119 ? (forward ? 1 : -1) : 0);

        for (var bit = start; bit is >= 0 and < 32; bit += forward ? 1 : -1) {
            if ((((uint)current >> (int)bit & 1) != 0) == high) {
                return (int)bit;
            }
        }

        return -1;
    }
}
