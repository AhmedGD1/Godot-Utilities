using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

public class TweenerComponent
{
    public enum UpdateType { Idle, Physics }

    public const string DefaultGroup = "Generic";
    private const int MaxPoolSize = 100;

    public Node Owner { get; private set; }

    private Dictionary<string, Curve> curves = new();
    private Dictionary<string, List<Tween>> tweenGroups = new();
    private Dictionary<string, float> groupSpeeds = new();
    private List<TweenerData> dataPool = [];

    private int poolSize = 0;
    private float speedScale = 1f;

    private UpdateType updateType = UpdateType.Idle;

    private Tween.TransitionType defaultTransition;
    private Tween.EaseType defaultEase;

    public Tween.TransitionType DefaultTransition => defaultTransition;
    public Tween.EaseType DefaultEaseType => defaultEase;

    public TweenerComponent(Node owner, Tween.TransitionType? defaultTransition = null, Tween.EaseType? defaultEase = null)
    {
        Owner = owner;
        this.defaultTransition = defaultTransition ?? Tween.TransitionType.Cubic;
        this.defaultEase = defaultEase ?? Tween.EaseType.In;

        PrewarmPool(10);
    }

    private void PrewarmPool(int count = 0)
    {
        for (int i = 0; i < count; i++)
        {
            if (poolSize >= MaxPoolSize)
                break;
            dataPool.Add(new TweenerData());
            poolSize++;
        }
    }

    private TweenerData AcquireData()
    {
        if (poolSize > 0)
        {
            poolSize--;

            TweenerData data = dataPool[^1];
            dataPool.Remove(data);
            data.Reset();

            return data;
        }
        return new TweenerData();
    }

    private void ReleaseData(TweenerData data)
    {
        if (poolSize >= MaxPoolSize)
            return;
        
        data.Reset();
        dataPool.Add(data);
        poolSize++;
    }

    public Dictionary<string, Variant> GetPoolStats()
    {
        return new()
        {
            {"Size", poolSize}, {"Capacity", MaxPoolSize}, {"Utilization", (float)(MaxPoolSize - poolSize) / MaxPoolSize}
        };
    }

    public void SetDefaultTransition(Tween.TransitionType transition, Tween.EaseType? easeType = null)
    {
        defaultTransition = transition;
        defaultEase = easeType ?? defaultEase;
    }   

    public void SetUpdate(UpdateType type)
    {
        updateType = type;
    }

    public void AddCurve(string key, Curve curve)
    {
        if (curves.ContainsKey(key))
        {
            GD.PushWarning($"There is already a curve with key: {key}");
            return;
        }
        curves[key] = curve;
    }

    public bool RemoveCurve(string key)
    {
        return curves.Remove(key);
    }

    public void SetGroupSpeed(string group, float value)
    {
        groupSpeeds[group] = value;
    }

    public TweenBuilder TweenProperty(GodotObject target, string property)
    {
        return new TweenBuilder(this, target, property);
    }

    public TweenSequence Sequence(GodotObject target, string group = DefaultGroup)
    {
        return new TweenSequence(this, target, group);
    }

    public void AnimateTo(GodotObject target, string property, Variant value, float duration = 1f, string group = DefaultGroup)
    {
        Animate(target, property, [value], [duration], defaultTransition, defaultEase, null, group);
    }

    public void Animate(GodotObject target, string property, Variant[] values, float[] durations, Tween.TransitionType? trans = null,
         Tween.EaseType? easeType = null, Action callback = null, string group = DefaultGroup, float delay = 0f, bool parallel = false)
    {
        Tween.TransitionType finalTransition = trans ?? defaultTransition;
        Tween.EaseType finalEase = easeType ?? defaultEase;

        TweenerData data = AcquireData();
        data.Group = group;
        data.Property = property;
        data.Values = values.ToList();
        data.Durations = durations.ToList();
        data.Transition = finalTransition;
        data.EaseType = finalEase;
        data.DelayOnStart = delay;
        data.Callback = callback;
        data.Parallel = parallel;

        AnimateInternalData(target, data);
    }

    public Tween TweenPath(GodotObject target, string property, string curveKey, Variant from, Variant to, float duration = 1f, 
        Action callback = null, string group = DefaultGroup, float delay = 0f, int loops = 1)
    {
        if (!GodotObject.IsInstanceValid(target))
        {
            GD.PushError("Target is not instance valid");
            return null;
        }

        if (!curves.TryGetValue(curveKey, out Curve curve))
        {
            GD.PushError($"Curve with key: {curveKey}, does not exist");
            return null;
        }

        var mode = updateType == UpdateType.Idle ? Tween.TweenProcessMode.Idle : Tween.TweenProcessMode.Physics;
        Tween newTween = Owner.CreateTween().SetLoops(loops).SetProcessMode(mode);

        if (delay > 0)
            newTween.TweenInterval(delay);

        float groupSpeed = groupSpeeds.TryGetValue(group, out var result) ? result : 1f;
        float finalSpeed = groupSpeed * speedScale;

        newTween.SetSpeedScale(finalSpeed);
        AddTweenToGroup(newTween, group);
        
        Callable method = Callable.From<float>(t => Interpolate(t, target, property, curve, from, to));
        newTween.TweenMethod(method, 0f, 1f, duration);
        newTween.Finished += () => CheckTweenCallback(newTween, callback, group);
    
        return newTween;
    }

    public void AnimateData(GodotObject target, TweenerData data)
    {
        AnimateInternalData(target, data, external: true);
    }

    private void AnimateInternalData(GodotObject target, TweenerData data, bool external = false)
    {
        List<Variant> values = !external ? data.Values : data.EditorValues.ToList();
        List<float> durations = !external ? data.Durations : data.EditorDurations.ToList();

        if (CheckErrors(target, data.Values, data.Durations))
        {
            if (!external)
                ReleaseData(data);
            return;
        }

        data.StartCall?.Invoke();

        var mode = updateType == UpdateType.Idle ? Tween.TweenProcessMode.Idle : Tween.TweenProcessMode.Physics;
        Tween newTween = Owner.CreateTween().SetProcessMode(mode);

        int count = Math.Min(data.Values.Count, data.Durations.Count);
        
        if (data.DelayOnStart > 0f)
            newTween.TweenInterval(data.DelayOnStart);
        newTween.SetTrans(data.Transition).SetEase(data.EaseType);
        newTween.SetLoops(data.Loops);
        newTween.SetParallel(data.Parallel);

        AddTweenToGroup(newTween, data.Group);

        float groupSpeed = groupSpeeds.TryGetValue(data.Group, out var result) ? result : 1f;
        float finalSpeed = groupSpeed * speedScale;

        newTween.SetSpeedScale(finalSpeed);

        for (int i = 0; i < count; i++)
            newTween.TweenProperty(target, data.Property, data.Values[i], data.Durations[i]);
        
        newTween.Finished += () =>
        {
            if (external)
                CheckTweenCallback(newTween, data.Callback, data.Group);
            else
                CheckTweenCallbackPool(newTween, data.Callback, data.Group, data, shouldRelease: true);  
        };
    }

    private void AddTweenToGroup(Tween targetTween, string group)
    {
        if (tweenGroups.TryGetValue(group, out var list))
        {
            list.Add(targetTween);
            return;
        }
        tweenGroups[group] = [targetTween];
    }

    public bool HasGroup(string group)
    {
        return tweenGroups.ContainsKey(group);
    }

    public void KillGroup(string group)
    {
        if (!tweenGroups.ContainsKey(group))
        {
            GD.PushWarning($"Group with name: {group}, does not exist");
            return;
        }

        foreach (Tween t in tweenGroups[group])
            if (GodotObject.IsInstanceValid(t))
                t.Kill();
        tweenGroups[group].Clear();
    }

    public void KillAll()
    {
        foreach (string group in tweenGroups.Keys.ToArray())
            KillGroup(group);
    }

    public void PauseGroup(string group)
    {
        SetGroupActive(group, false);
    }

    public void PauseAll()
    {
        foreach (string group in tweenGroups.Keys.ToArray())
            PauseGroup(group);
    }

    public void ResumeAll()
    {
        foreach (string group in tweenGroups.Keys.ToArray())
            ResumeGroup(group);
    }

    public void ResumeGroup(string group)
    {
        SetGroupActive(group, true);
    }

    public bool IsAnimating(string group)
    {
        if (!tweenGroups.TryGetValue(group, out var list))
            return false;
        
        int validCount = 0;

        foreach (Tween t in list)
            if (GodotObject.IsInstanceValid(t) && t.IsRunning())
                validCount++;
        return validCount > 0;
    }

    public void CleanUp()
    {
        KillAll();
        curves.Clear();
        tweenGroups.Clear();
        groupSpeeds.Clear();
        dataPool.Clear();
        poolSize = 0;
    }

    public string[] GetActiveGroups()
    {
        return tweenGroups.Keys.ToArray();
    }

    public Tween[] GetGroupTweens(string group)
    {
        if (!tweenGroups.TryGetValue(group, out var list))
            return [];
        return list.ToArray();
    }

    private void SetGroupActive(string group, bool active)
    {
        if (!tweenGroups.TryGetValue(group, out var list))
        {
            GD.PushError($"Group with name: {group}, does not exist");
            return;
        }

        foreach (Tween t in list)
            if (GodotObject.IsInstanceValid(t))
                if (active) t.Play();
                else t.Pause();
    }

    private void Interpolate(float t, GodotObject target, string property, Curve curve, Variant a, Variant b)
    {
        float sample = curve.Sample(t);
        
        Variant result = a.VariantType switch
        {
            Variant.Type.Float => Mathf.Lerp((float)a, (float)b, sample),
            Variant.Type.Vector2 => ((Vector2)a).Lerp((Vector2)b, sample),
            Variant.Type.Vector3 => ((Vector3)a).Lerp((Vector3)b, sample),
            Variant.Type.Color => ((Color)a).Lerp((Color)b, sample),
            Variant.Type.Quaternion => ((Quaternion)a).Slerp((Quaternion)b, sample),
            _ => throw new ArgumentException($"Unsupported type {a.VariantType} for interpolation.")
        };

        target.Set(property, result);
    }

    private void CheckTweenCallback(Tween targetTween, Action callback, string group)
    {
        callback?.Invoke();
        tweenGroups[group].Remove(targetTween);

        if (tweenGroups[group].Count == 0)
            tweenGroups.Remove(group);
    }

    private void CheckTweenCallbackPool(Tween targetTween, Action callback, string group, TweenerData data, bool shouldRelease)
    {
        callback?.Invoke();
        tweenGroups[group].Remove(targetTween);

        if (tweenGroups[group].Count == 0)
            tweenGroups.Remove(group);
        
        if (shouldRelease)
            ReleaseData(data);
    }

    private bool CheckErrors(GodotObject target, List<Variant> values, List<float> durations)
    {
        if (!GodotObject.IsInstanceValid(target))
        {
            GD.PushError("Target of this tween is not instance valid");
            return true;
        }

        if (values.Count == 0 || durations.Count == 0)
        {
            GD.PushError("Can not pass an empty array to the tween");   
            return true;
        }

        if (values.Count != durations.Count)
        {
            GD.PushWarning("Values and Durations Size is different, some animations will not be shown");
            return true;
        }

        return false;
    }

    public class TweenBuilder
    {
        private TweenerComponent component;
        private TweenerData data;
        private GodotObject target;

        public TweenBuilder(TweenerComponent component, GodotObject target, string property)
        {
            this.component = component;
            this.target = target;
            
            data = component.AcquireData();
            data.Property = property;
            data.Transition = component.DefaultTransition;
            data.EaseType = component.defaultEase;
            data.Group = DefaultGroup;
        }

        public TweenBuilder To(params Variant[] value)
        {
            data.Values = value.ToList();
            return this;
        }

        public TweenBuilder Durations(params float[] value)
        {
            data.Durations = value.ToList();
            return this;
        }

        public TweenBuilder Transition(Tween.TransitionType type)
        {
            data.Transition = type;
            return this;
        }

        public TweenBuilder WithEase(Tween.EaseType type)
        {
            data.EaseType = type;
            return this;
        }

        public TweenBuilder Delay(float value)
        {
            data.DelayOnStart = value;
            return this;
        }

        public TweenBuilder Loops(int value)
        {
            data.Loops = value;
            return this;
        }

        public TweenBuilder Parallel(bool active)
        {
            data.Parallel = active;
            return this;
        }

        public TweenBuilder Group(string group)
        {
            data.Group = group;
            return this;
        }

        public TweenBuilder OnStart(Action action)
        {
            data.StartCall = action;
            return this;
        }

        public TweenBuilder OnComplete(Action action)
        {
            data.Callback = action;
            return this;
        }

        public void Start()
        {
            if (data.Values.Count == 0)
            {
                GD.PushError("TweenBuilder: No values specified. Use .to() before .start()");
                component.ReleaseData(data);
                return;               
            }

            if (data.Durations.Count == 0)
                data.Durations = Enumerable.Repeat(1f, data.Values.Count).ToList();

            component.AnimateInternalData(target, data);
        }

        public TweenBuilder Smooth()
        {
            EaseInOut().Sine();
            return this;
        }

        public TweenBuilder Sine()
        {
            Transition(Tween.TransitionType.Sine);
            return this;
        }

        public TweenBuilder Bounce()
        {
            Transition(Tween.TransitionType.Bounce);
            return this;
        }

        public TweenBuilder Elastic()
        {
            Transition(Tween.TransitionType.Elastic);
            return this;
        }

        public TweenBuilder Linear()
        {
            Transition(Tween.TransitionType.Linear);
            return this;
        }

        public TweenBuilder Spring()
        {
            Transition(Tween.TransitionType.Spring);
            return this;
        }

        public TweenBuilder Cubic()
        {
            Transition(Tween.TransitionType.Cubic);
            return this;
        }

        public TweenBuilder EaseInOut()
        {
            WithEase(Tween.EaseType.InOut);
            return this;
        }

        public TweenBuilder EaseOutIn()
        {
            WithEase(Tween.EaseType.OutIn);
            return this;
        }

        public TweenBuilder EaseIn()
        {
            WithEase(Tween.EaseType.In);
            return this;
        }

        public TweenBuilder EaseOut()
        {
            WithEase(Tween.EaseType.Out);
            return this;
        }
    }

    public class TweenSequence
    {
        private TweenerComponent component;
        private GodotObject target;
        private string group;
        private List<SequenceStep> steps = new();

        public TweenSequence(TweenerComponent component, GodotObject target, string group)
        {
            this.component = component;
            this.target = target;
            this.group = group;
        }

        public TweenSequence Then(string property, Variant value, float duration = 1f)
        {
            steps.Add(SequenceStep.Animate(property, value, duration, component.defaultTransition, component.DefaultEaseType));
            return this;
        }

        public TweenSequence Wait(float duration)
        {
            steps.Add(SequenceStep.WaitStep(duration));
            return this;
        }

        public TweenSequence OnComplete(Action callback)
        {
            steps.Add(SequenceStep.CallbackStep(callback));
            return this;
        }

        public TweenSequence WithTransition(Tween.TransitionType type)
        {
            if (steps.Count > 0)
                steps[^1].Transition = type;
            return this;
        }

        public TweenSequence WithEase(Tween.EaseType type)
        {
            if (steps.Count > 0)
                steps[^1].EaseType = type;
            return this;
        }

        public void Start()
        {
            ExecuteNextStep(0);
        }

        private void ExecuteNextStep(int index)
        {
            if (index >= steps.Count)
                return;
            
            SequenceStep step = steps[index];

            switch (step.Type)
            {
                case SequenceStep.StepType.Animate:
                    component.Animate(
                        target, step.Property, [step.Value], [step.Duration], step.Transition, step.EaseType,
                        () => ExecuteNextStep(index + 1), group
                    );
                    break;
                
                case SequenceStep.StepType.Wait:
                    component.Owner.GetTree().CreateTimer(step.Duration).Timeout += () => ExecuteNextStep(index + 1);
                    break;
                
                case SequenceStep.StepType.Callback:
                    step.Callback?.Invoke();
                    ExecuteNextStep(index + 1);
                    break;
            }
        }

        //---------------------------------
        public TweenSequence Bounce()
        {
            WithTransition(Tween.TransitionType.Bounce);
            return this;
        }

        public TweenSequence Elastic()
        {
            WithTransition(Tween.TransitionType.Elastic);
            return this;
        }
    
        public TweenSequence Spring()
        {
            WithTransition(Tween.TransitionType.Spring);
            return this;
        }

        public TweenSequence Cubic()
        {
            WithTransition(Tween.TransitionType.Cubic);
            return this;
        }

        public TweenSequence Sine()
        {
            WithTransition(Tween.TransitionType.Sine);
            return this;
        }

        public TweenSequence Quad()
        {
            WithTransition(Tween.TransitionType.Quad);
            return this;
        }

        public TweenSequence EaseIn()
        {
            WithEase(Tween.EaseType.In);
            return this;
        }

        public TweenSequence EaseOut()
        {
            WithEase(Tween.EaseType.Out);
            return this;
        }
    }

    public class SequenceStep
    {
        public enum StepType { Animate, Wait, Callback }

        public StepType Type { get; set; }
        public string Property { get; set; }
        public Variant Value { get; set; }
        public float Duration { get; set; } = 1f;
        public Tween.TransitionType Transition { get; set; }
        public Tween.EaseType EaseType { get; set; }
        public Action Callback { get; set; }

        public static SequenceStep Animate(string property, Variant value, float duration, Tween.TransitionType trans, Tween.EaseType easeType)
        {
            SequenceStep step = new SequenceStep()
            {
                Type = StepType.Animate,
                Property = property,
                Value = value,
                Duration = duration,
                Transition = trans,
                EaseType = easeType  
            };

            return step;
        }

        public static SequenceStep WaitStep(float duration)
        {
            SequenceStep step = new SequenceStep()
            {
                Type = StepType.Wait,
                Duration = duration
            };

            return step;
        }

        public static SequenceStep CallbackStep(Action callback)
        {
            SequenceStep step = new SequenceStep()
            {
                Type = StepType.Callback,
                Callback = callback
            };

            return step;
        }
    }
}
