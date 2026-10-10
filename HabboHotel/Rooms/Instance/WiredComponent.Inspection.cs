using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Variables;

namespace Plus.HabboHotel.Rooms.Instance;

public partial class WiredComponent
{
    // Inspection captures attached entities without firing a stack or inventing trigger/event payloads.
    internal WiredVariableFrame CaptureVariableInspectionFrame() => WiredVariableRuntimeFrames.CaptureInspection(
        new WiredRuntimeContext(_room, new(WiredEventKind.Inspection), _targets, this)
        { NowMilliseconds = _engine.NowMilliseconds });
}
