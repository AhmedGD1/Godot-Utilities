using Godot;
using System;
using System.Collections.Generic;

namespace Utilities.Tweening;

public static class GTweenExtensions
{
    public static TweenBuilder TweenProperty(this GodotObject @object, string property)
    {
        return GTween.GetPool<TweenBuilder>(@object, property);
    }

    public static Tween TweenProperty(this GodotObject @object, string property, Action<TweenBuilder> config)
    {
        var builder = GTween.GetPool<TweenBuilder>(@object, property);
        config(builder);
        return builder.Start();
    }

    public static PathBuilder TweenPath(this GodotObject @object, string property, Curve curve)
    {
        var builder = GTween.GetPool<PathBuilder>(@object, property);
        builder.Curve = curve;

        return builder;
    }

    public static Tween TweenPath(this GodotObject @object, string property, Curve curve, Action<PathBuilder> config)
    {
        var builder = GTween.GetPool<PathBuilder>(@object, property);
        builder.Curve = curve;

        config(builder);
        return builder.Start();
    }

    public static void KillTweens(this GodotObject @object)
    {
        var buildersToKill = new List<IBuilder>();

        foreach (var builder in GTween.activeBuilders)
        {
            if (builder.Target == @object)
            {
                buildersToKill.Add(builder);
            }
        }
        
        foreach (var builder in buildersToKill)
        {
            builder.ActiveTween?.Kill();
            GTween.ReturnToPool(builder);
        }
    }

    public static void KillSafe(this IBuilder builder)
    {
        builder.ActiveTween?.Kill();
        GTween.ReturnToPool(builder);
    }
}

public interface IBuilder
{
    event Action Completed;

    GodotObject Target { get; set; }
    Tween ActiveTween { get; set; }
    string Property { get; set; }
    string Group { get; set; }
    int Loops { get; set; }

    void Pause();
    void Resume();
    void Cancel();

    void Reset();
    void Update(double delta);
    void AddSub(Action callback);

    Tween Start();
    Tween Replay();
    
    float GetTotalDuration();
}