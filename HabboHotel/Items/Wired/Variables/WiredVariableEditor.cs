using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>Overlay live room-variable values when reopening an editor; the saved definition is not a value cache.</summary>
public sealed class WiredVariableEditor(WiredVariableModule variables)
{
    public WiredConfiguration ForDisplay(string name, uint definitionId, WiredConfiguration saved)
    {
        if (name != "wf_var_room" || saved.IntParams.Length != 2) return saved;
        var value = variables.Read(new(WiredVariableTarget.Global, $"custom:{definitionId}"), new(WiredVariableTarget.Global, 0, 0), new(variables.RoomId, []));
        return value is null ? saved : saved with { IntParams = saved.IntParams.SetItem(1, value.Value) };
    }

    /// <summary>After a definition configuration save, persist its explicit room value through normal authorization and change events.</summary>
    public bool SaveGlobalValue(uint definitionId, int value) => variables.Mutate(new(WiredVariableTarget.Global, $"custom:{definitionId}"),
        new(WiredVariableTarget.Global, 0, 0), WiredVariableMutation.Set, value, new(variables.RoomId, []));
}
