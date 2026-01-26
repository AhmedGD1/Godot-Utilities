using Godot;

namespace Utilities.Particles;

[GlobalClass]
public partial class ParticlePreset : Resource
{
    [Export]
    public string id;

    [Export]
    public PackedScene scene;

    [Export]
    public int poolSize = 5;
}

