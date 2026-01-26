using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

namespace Utilities.Tweening;

public partial class GTween
{
    public static bool IsGroupActive(string group)
    {
        return builderGroups.ContainsKey(group) && builderGroups[group].Count > 0;
    }

    public static int GetGroupTweenCount(string group)
    {
        if (!builderGroups.TryGetValue(group, out var builders))
            return 0;
        return builders.Count;
    }

    public static void KillGroup(string group)
    {
        if (!builderGroups.TryGetValue(group, out var builders))
            return;

        var builderList = builders.ToList();
        
        foreach (var builder in builderList)
            builder.KillSafe();
    }

    public static void KillGroup(string group, params IBuilder[] excludedBuilders)
    {
        if (!builderGroups.TryGetValue(group, out var builders))
            return;

        var excludedSet = new HashSet<IBuilder>(excludedBuilders);
        var builderList = builders.ToList();

        foreach (var builder in builderList)
        {
            if (!excludedSet.Contains(builder))
                builder.KillSafe();
        }
    }

    public static void CompleteGroup(string group)
    {
        ForEach(group, b => b.ActiveTween?.CustomStep(9999));
    }

    public static void PauseGroup(string group)
    {
        ForEach(group, b => b.Pause());
    }

    public static void ResumeGroup(string group)
    {
        ForEach(group, b => b.Resume());
    }

    public static void ForEach(string group, Action<IBuilder> config)
    {
        if (!builderGroups.TryGetValue(group, out var builders))
            return;

        var builderList = builders.ToList();
        
        foreach (var builder in builderList)
            config(builder);
    }

    public static IEnumerable<IBuilder> GetGroupBuilders(string group)
    {
        if (!builderGroups.TryGetValue(group, out var builders))
            return Enumerable.Empty<IBuilder>();
        
        return builders.ToList();
    }

    public static IEnumerable<T> GetGroupBuilders<T>(string group) where T : IBuilder
    {
        if (!builderGroups.TryGetValue(group, out var builders))
            return Enumerable.Empty<T>();
        
        return builders.OfType<T>().ToList();
    }

    public static IEnumerable<IBuilder> GetBuildersForTarget(GodotObject target)
    {
        return activeBuilders.Where(b => b.Target == target).ToList();
    }

    public static IEnumerable<string> GetActiveGroups()
    {
        return builderGroups.Keys.ToList();
    }

    #region Enum Based Group Management
    public static void KillGroup<TGroup>(TGroup group) where TGroup : Enum
    {
        KillGroup(group.ToString());
    }

    public static void PauseGroup<TGroup>(TGroup group) where TGroup : Enum
    {
        PauseGroup(group.ToString());
    }

    public static void ResumeGroup<TGroup>(TGroup group) where TGroup : Enum
    {
        ResumeGroup(group.ToString());
    }

    public static void CompleteGroup<TGroup>(TGroup group) where TGroup : Enum
    {
        CompleteGroup(group.ToString());
    }

    public static bool IsGroupActive<TGroup>(TGroup group) where TGroup : Enum
    {
        return IsGroupActive(group.ToString());
    }

    public static int GetGroupTweenCount<TGroup>(TGroup group) where TGroup : Enum
    {
        return GetGroupTweenCount(group.ToString());
    }

    public static IEnumerable<IBuilder> GetGroupBuilders<TGroup>(TGroup group) where TGroup : Enum
    {
        return GetGroupBuilders(group.ToString());
    }

    public static void ForEach<TGroup>(TGroup group, Action<IBuilder> config) where TGroup : Enum
    {
        ForEach(group.ToString(), config);
    }
    #endregion
}