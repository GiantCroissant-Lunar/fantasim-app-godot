using Godot;

public partial class Main : Node
{
    public override void _Ready()
    {
        GD.Print("[Main] Host autoload should have composed all services.");
    }
}
