using System.Collections.Immutable;
using Plus.HabboHotel.Items.Wired.Variables;

namespace Plus.Communication.Packets.Outgoing.WiredVariables;

internal static class WiredVariableWireCapture
{
    internal static WiredVariableDescription Capture(WiredVariableDescription variable) =>
        variable with
        {
            TextConnector = variable.TextConnector.ToImmutableDictionary()
        };
}
