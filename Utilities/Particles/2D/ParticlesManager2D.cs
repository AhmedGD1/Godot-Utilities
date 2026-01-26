using Godot;
using System.Collections.Generic;

namespace Utilities.Particles;

public partial class ParticlesManager2D : Node
{
    public static ParticlesManager2D Instance { get; private set; }

    private static Dictionary<string, Queue<GpuParticles2D>> pool = new();
    private static readonly Dictionary<string, ParticlePreset> presets = new();
    private static readonly Dictionary<string, GpuParticles2D> references = new();

    private static readonly HashSet<GpuParticles2D> activeReferences = new();
    private static readonly HashSet<GpuParticles2D> activePooled = new();

    public override void _Ready()
    {
        Instance = this;
    }

    public override void _ExitTree()
    {
        pool = new();
    }

    #region Ref Methods
    public void RegisterRef(string name, GpuParticles2D particles)
    {
        if (references.ContainsKey(name))
        {
            GD.PushWarning($"Invalid Registration, ref {name} does exist already");
            return;
        }

        references[name] = particles;

        particles.Finished += () =>
            activeReferences.Remove(particles);  
    }
    #endregion

    #region Preset Methods
    public void RegisterPreset(ParticlePreset preset)
    {
        if (preset == null || preset.scene == null)
        {
            GD.PushError("Invalid preset");
            return;
        }
       
        if (presets.ContainsKey(preset.id))
        {
            GD.PushWarning($"Preset {preset.id} already registered");
            return;
        }

        presets[preset.id] = preset;
        pool[preset.id] = new Queue<GpuParticles2D>();
        
        GpuParticles2D particles;

        for (int i = 0; i < preset.poolSize; i++)
        {
            particles = CreateNewParticle(preset);
            pool[preset.id].Enqueue(particles);
        }
    }

    private void ReturnToPool(string id, GpuParticles2D particles)
    {
        particles.Hide();
        pool[id].Enqueue(particles);
    }

    private GpuParticles2D GetPooled(string id)
    {
        if (!presets.TryGetValue(id, out ParticlePreset preset))
            return null;
        
        if (pool[id].Count == 0)
            return CreateNewParticle(preset);
        return pool[id].Dequeue();
    }

    private GpuParticles2D CreateNewParticle(ParticlePreset preset)
    {
        var particles = preset.scene.Instantiate<GpuParticles2D>();
        particles.Emitting = true;
        particles.OneShot = true;
        
        particles.Finished += () =>
        {
            ReturnToPool(preset.id, particles);
            activePooled.Remove(particles);
        };

        GetTree().CurrentScene.AddChild(particles);
        return particles;
    }
    #endregion

}
