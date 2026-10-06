namespace Plus.HabboHotel.Items.Wired;

// Some legacy effects have an intrinsic delay independent of the stored editor value.
internal interface IWiredActionDelay
{
    long DelayMilliseconds
    {
        get;
    }
}
