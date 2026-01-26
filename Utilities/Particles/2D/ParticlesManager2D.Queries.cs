using System;
using Godot;

namespace Utilities.Particles;

public partial class ParticlesManager2D
{
    public void PlayPreset(string id, Vector2 pos, Action<GpuParticles2D> config = null)
    {
        var particles = GetPooled(id);
        config?.Invoke(particles);

        particles.GlobalPosition = pos;
        particles.Emitting = true;
        particles.Show();
        particles.Restart();

        activePooled.Add(particles);
    }

    public void PlayRef(string name, Action<GpuParticles2D> config = null)
    {
        if (!ValidateRef(name, out var particles))
            return;

        config?.Invoke(particles);
        particles.Emitting = true;

        activeReferences.Add(particles);
    }

    public GpuParticles2D GetRef(string name)
    {
        return ValidateRef(name, out var result) ? result : null;
    }

    #region Validation
    private bool ValidateRef(string name, out GpuParticles2D particles)
    {
        if (!references.TryGetValue(name, out var value))
        {
            GD.PushError("Invalid Ref Name");
            particles = default;
            return false;
        }
        particles = value;
        return true;
    }
    #endregion

    #region Stop
    public void StopRef(string name)
    {
        if (ValidateRef(name, out var particles))
            particles.Emitting = false;
    }

    public void StopAllRef()
    {
        foreach (var particles in activeReferences)
            particles.Emitting = false;
    }

    public void StopAllPooled()
    {
        foreach (var particles in activePooled)
            particles.Emitting = false;
    }
    #endregion

    public int GetActivePooledCount() => activePooled.Count;
    public int GetActiveRefsCount() => activeReferences.Count;
    public int GetPoolSize(string id) => pool.TryGetValue(id, out var queue) ? queue.Count : 0;

    public void PrintDebugInfo()
    {
        GD.Print($"=== Particle Manager Stats ===");
        GD.Print($"Active Pooled: {activePooled.Count}");
        GD.Print($"Active Refs: {activeReferences.Count}");
    }
}

