using Godot;
using System.Collections.Generic;

namespace Utilities.InputManagement;

public partial class InputManager : Node
{
    [Signal]
    public delegate void EscapeEventHandler();

    private double DefaultBufferTime = 0.15;

    public static InputManager Instance { get; private set; }
    public static BufferManager Buffer { get; private set; }

    public bool InputEnabled { get; set; } = true;

    private Dictionary<string, StringName> pressedToSignal = new()
    {
        {"ui_text_clear_carets_and_selection", SignalName.Escape }
    };

    private Dictionary<string, StringName> releasedToSignal = new()
    {
        
    };

    public override void _EnterTree()
    {
        Instance = this;
        Buffer = new BufferManager();
    }

    public override void _Process(double delta)
    {
        Buffer.Update(delta);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!InputEnabled)
            return;
        
        foreach (var (action, signal) in pressedToSignal)
            if (@event.IsActionPressed(action))
                EmitSignal(signal);
        
        foreach (var (action, signal) in releasedToSignal)
            if (@event.IsActionReleased(action))
                EmitSignal(signal);
    }

    public void Enable() => InputEnabled = true;
    public void Disable() => InputEnabled = false;

    public Vector2 GetMoveDirection()
    {
        if (!InputEnabled) return Vector2.Zero;
        return Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
    }
}

