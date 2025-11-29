using System;
using System.Collections.Generic;
using System.Linq;

namespace Godot.FSM;

public enum FSMProcessMode
{
    Physics,
    Idle
}

public enum FSMLockMode
{
    None,
    Full,
    Transition
}

public class StateMachine<T> where T : Enum
{
    private const int MAX_QUEUED_TRANSITIONS = 50;

    public event Action<T, T> StateChanged;
    public event Action<T> TimeoutBlocked;
    public event Action<T> StateTimeout;
    public event Action<T, T> TransitionTriggered;

    private Dictionary<T, State> states = new();
    private Dictionary<string, object> globalData = new();

    private Dictionary<T, List<Transition>> transitions = new();
    private List<Transition> globalTransitions = new();
    private List<Transition> cachedSortedTransitions = new();
    private Queue<T> pendingTransitions = new();

    private State currentState;

    private T initialId;
    private T previousId;

    private bool initialized;
    private bool hasPreviousState;
    private bool paused;
    private bool transitionDirty = true;
    private bool isTransitioning;


    private float stateTime;
    private float lastStateTime;
    

    public State AddState(T id)
    {
        if (states.ContainsKey(id))
        {
            GD.PushError($"State with id: {id} already exists");
            return null;
        }

        var state = new State(id);
        states[id] = state;

        if (!initialized)
        {
            initialId = id;
            initialized = true;
        }

        state.SetRestart(id);
        return state;
    }

    public void Start()
    {
        if (initialized)
            ChangeStateInternal(initialId, ignoreExit: true);
    }

    public bool RemoveState(T id)
    {
        if (!states.ContainsKey(id))
        {
            GD.PushWarning($"State with id: {id} does not exist to be removed !");
            return false;
        }

        states.Remove(id);

        if (initialId.Equals(id))
            initialized = false;
        
        if (currentState?.Id.Equals(id) ?? false)
        {
            if (states.Count > 0)
            {
                SetInitialId(states.Values.First().Id);
                Reset();
            }
            else
            {
                currentState = null;
                initialized = false;
                hasPreviousState = false;
            }
        }

        transitions.Remove(id);

        foreach (var kvp in transitions.ToList())
        {
            kvp.Value.RemoveAll(t => t.To.Equals(id));

            if (kvp.Value.Count == 0)
                transitions.Remove(kvp.Key);
        }

        globalTransitions.RemoveAll(t => t.To.Equals(id));

        ReSortTransitions();
        return true;
    }

    public bool Reset()
    {
        if (states.Count == 0)
        {
            GD.PushWarning("State Machine can Reset while being Empty !");
            return false;
        }

        if (!initialized)
        {
            GD.PushWarning("State Machine not initialized - call SetInitialId() first");
            return false;
        }

        ChangeStateInternal(initialId);
        hasPreviousState = false;
        previousId = default;
        return true;
    }

    public void SetInitialId(T id)
    {
        if (!states.ContainsKey(id))
        {
            GD.PushError($"State with this id does not exist");
            return;
        }

        initialId = id;
        initialized = true;
    }

    public void RestartCurrentState(bool ignoreExit = false, bool ignoreEnter = false)
    {
        if (currentState == null)
        {
            GD.PushWarning("Can't restart current state as it does not exist");
            return;
        }

        ResetStateTime();

        if (!ignoreExit && !currentState.IsLocked()) currentState.Exit?.Invoke();
        if (!ignoreEnter) currentState.Enter?.Invoke();
    }

    public void ResetStateTime()
    {
        lastStateTime = stateTime;
        stateTime = 0f;
    }

    public bool TryChangeState(T id, Func<bool> condition = null)
    {
        if (!(condition?.Invoke() ?? true))
            return false;

        if (!states.ContainsKey(id))
            return false;

        ChangeStateInternal(id);
        return true;
    }

    public bool TryGoBack()
    {
        if (!hasPreviousState || !states.ContainsKey(previousId) || (currentState?.IsLocked() ?? false))
        {
            GD.PushError("Can't go back to previous state");
            return false;
        }

        ChangeStateInternal(previousId);
        return true;
    }

    private void ChangeStateInternal(T id, bool ignoreExit = false)
    {
        if (isTransitioning)
        {
            if (pendingTransitions.Count >= MAX_QUEUED_TRANSITIONS)
            {
                GD.PushError($"Too many queued transitions ({MAX_QUEUED_TRANSITIONS})! Possible infinite loop?");
                return;
            }
            pendingTransitions.Enqueue(id);
            return;
        }

        if (!states.TryGetValue(id, out State value))
        {
            GD.PushWarning($"Can not change state to {id} as it does not exist");
            return;
        }

        isTransitioning = true;

        try
        {
            bool canExit = !ignoreExit && currentState != null && !currentState.IsLocked();
            if (canExit) currentState.Exit?.Invoke();

            lastStateTime = stateTime;
            stateTime = 0f;

            if (currentState != null)
            {
                previousId = currentState.Id;
                hasPreviousState = true;
            }

            currentState = value;
            currentState.Enter?.Invoke();

            ReSortTransitions();

            if (initialized)
                StateChanged?.Invoke(previousId, currentState.Id);
        
            while (pendingTransitions.Count > 0)
            {
                var nextId = pendingTransitions.Dequeue();
                isTransitioning = false;
                ChangeStateInternal(nextId);
                isTransitioning = true;
            }
        }
        finally
        {
            isTransitioning = false;
        }
    }

    public Transition AddTransition(T from, T to)
    {
        if (!states.ContainsKey(to))
        {
            GD.PushError($"Can not transition as (To state) does not exist");
            return null;
        }

        if (!transitions.ContainsKey(from))
            transitions[from] = new();
        
        Transition transition = new Transition(from, to);
        transitions[from].Add(transition);

        ReSortTransitions();
        return transition;
    }

    public void AddTransitions(T[] from, T to, Predicate<StateMachine<T>> condition)
    {
        for (int i = 0; i < from.Length; i++)
            AddTransition(from[i], to).SetCondition(condition);
    }

    public Transition AddGlobalTransition(T to)
    {
        if (!states.ContainsKey(to))
        {
            GD.PushError($"Can not transition as (To state) does not exist");
            return null;
        }

        Transition transition = new Transition(default, to);
        globalTransitions.Add(transition);

        ReSortTransitions();
        return transition;
    }

    public bool RemoveTransition(T from, T to)
    {
        if (!transitions.TryGetValue(from, out List<Transition> value))
        {
            GD.PushWarning("The (from transition) does not exist");
            return false;
        }

        int removed = value.RemoveAll(t => t.To.Equals(to));

        if (value.Count == 0)
            transitions.Remove(from);
        
        if (removed == 0)
            GD.PushError($"No Transition Was Found Between: {from} -> {to}");
        
        ReSortTransitions();
        return removed > 0;
    }

    public bool RemoveGlobalTransition(T to)
    {
        int removed = globalTransitions.RemoveAll(t => t.To.Equals(to));

        if (removed == 0)
        {
            GD.PushWarning($"No Global Transition Was Found to state: {to}");
            return false;
        }

        ReSortTransitions();
        return true;
    }

    public void ClearTransitionsFrom(T id)
    {
        transitions.Remove(id);
        ReSortTransitions();
    }

    public void ClearTransitions()
    {
        transitions.Clear();
        ReSortTransitions();
    }

    public void ClearGlobalTransitions()
    {
        globalTransitions.Clear();
        ReSortTransitions();
    }

    private void ReSortTransitions()
    {
        transitionDirty = true;
    }

    public void Process(FSMProcessMode mode, double delta)
    {
        if (paused || currentState == null) return;

        if (currentState.ProcessMode == mode)
        {
            stateTime += (float)delta;
            currentState.Update?.Invoke(delta);
            CheckTransitions();
        }
    }

    private void CheckTransitions()
    {
        if (currentState == null) return;

        bool timeoutTriggered = currentState.Timeout > 0f && stateTime >= currentState.Timeout;

        if (timeoutTriggered)
        {
            if (currentState.IsFullyLocked())
            {
                TimeoutBlocked?.Invoke(currentState.Id);
                return;
            }

            var restartId = currentState.RestartId;
            var fromId = currentState.Id;

            if (!states.ContainsKey(restartId))
            {
                GD.PushError($"RestartId {restartId} doesn't exist for state {fromId}");
                return;
            }

            StateTimeout?.Invoke(fromId);
            ChangeStateInternal(restartId);
            TransitionTriggered?.Invoke(fromId, restartId);
            return;
        }

        if (currentState.TransitionBlocked()) return;

        RebuildTransitionCache();

        if (cachedSortedTransitions.Count > 0)
            CheckTransitionLoop(cachedSortedTransitions);
    }

    private void CheckTransitionLoop(List<Transition> candidateTransitions)
    {
        foreach (Transition transition in candidateTransitions)
        {
            float requiredTime = transition.OverrideMinTime > 0f ? transition.OverrideMinTime : currentState.MinTime;
            bool timeRequirementMet = stateTime > requiredTime || transition.ForceInstantTransition;

            if (timeRequirementMet && (transition.Condition?.Invoke(this) ?? true))
            {
                ChangeStateInternal(transition.To);
                transition.Triggered?.Invoke();
                TransitionTriggered?.Invoke(transition.From, transition.To);
                return;
            }
        }
    }

    private void RebuildTransitionCache()
    {
        if (!transitionDirty) return;

        cachedSortedTransitions.Clear();

        if (currentState != null && transitions.TryGetValue(currentState.Id, out var currentTransitions))
            cachedSortedTransitions.AddRange(currentTransitions);
        
        cachedSortedTransitions.AddRange(globalTransitions);
        cachedSortedTransitions.Sort(Transition.Compare);

        transitionDirty = false;
    }

    public bool SetData(string id, object data)
    {
        if (string.IsNullOrEmpty(id)) 
            return false;
        globalData[id] = data;
        return true;
    }

    public bool RemoveGlobalData(string id)
    {
        if (!globalData.ContainsKey(id))
            return false;
        globalData.Remove(id);
        return true;
    }

    public bool TryGetData<TData>(string id, out TData data)
    {
        if (globalData.TryGetValue(id, out var value) && value is TData castValue)
        {
            data = castValue;
            return true;
        }
        data = default;
        return false;
    }

    public bool IsActive() => !paused;
    public void Pause() => paused = true;
    public void Resume(bool resetTime = false)
    {   
        if (resetTime)
            ResetStateTime();
        paused = false;
    }

    /// <summary>
    /// Gets how long the state machine was in the previous state before transitioning
    /// </summary>
    public float GetPreviousStateTime() => hasPreviousState ? lastStateTime : -1f;
    public float GetStateTime() => stateTime;
    public float GetMinStateTime() => currentState?.MinTime ?? -1f;
    public float GetRemainingTime() => currentState?.Timeout > 0f ? Mathf.Max(0f, currentState.Timeout - stateTime) : -1f;

    public State GetState(T id) => states.TryGetValue(id, out var result) ? result : null;
    public State GetStateWithTag(string tag) => states.Values.FirstOrDefault(state => state.Tags.Contains(tag));

    public float GetTimeoutProgress()
    {
        if (currentState == null || currentState.Timeout <= 0f)
            return -1f;
        return Mathf.Clamp(stateTime / currentState.Timeout, 0f, 1f);
    }

    public T GetCurrentId() => currentState != null ? currentState.Id : default;
    public T GetInitialId() => initialized ? initialId : default;
    public T GetPreviousId() => hasPreviousState ? previousId : throw new InvalidOperationException("No previous state");

    public bool HasTransition(T from, T to) => transitions.TryGetValue(from, out var list) && list.Any(t => t.To.Equals(to));
    public bool HasTransitionFrom(T from) => transitions.TryGetValue(from, out var list) && list.Count > 0;
    public bool HasGlobalTransition(T id) => globalTransitions.Any(t => t.To.Equals(id));

    public bool HasState(T id) => states.ContainsKey(id);
    public bool IsCurrentState(T id) => currentState?.Id.Equals(id) ?? false;
    public bool IsPreviousState(T id) => hasPreviousState && Equals(previousId, id);
    public bool IsInStateWithTag(string tag) => currentState?.Tags.Contains(tag) ?? false;

    public string DebugCurrentTransition()
    {
        if (currentState == null) return "Not Started";
        return hasPreviousState 
            ? $"{previousId} -> {currentState.Id}" 
            : $"[Initial] -> {currentState.Id}";
    }

    public string DebugAllTransitions()
    {
        var result = new List<string>();
        foreach (var kvp in transitions)
            foreach (var t in kvp.Value)
                result.Add($"{t.From} -> {t.To} (Priority: {t.Priority})");

        foreach (var t in globalTransitions)
            result.Add($"GLOBAL -> {t.To} (Priority: {t.Priority})");

        return string.Join("\n", result);
    }

    public string DebugAllStates()
    {
        var result = new List<string>();
        foreach (State state in states.Values)
            result.Add(state.Id.ToString());
        return string.Join("\n", result);
    }

    public class State(T id)
    {
        public T Id { get; private set; } = id;
        public T RestartId { get; private set; }

        public float MinTime { get; private set; }
        public float Timeout { get; private set; } = -1f;

        public Action<double> Update { get; private set; }
        public Action Enter { get; private set; }
        public Action Exit { get; private set; }

        public FSMProcessMode ProcessMode { get; private set; }
        public FSMLockMode LockMode { get; private set; }

        private readonly HashSet<string> tags = new();
        private readonly Dictionary<string, object> data = new();

        public IReadOnlyCollection<string> Tags => tags;
        public IReadOnlyDictionary<string, object> Data => data;

        public State OnUpdate(Action<double> update)
        {
            Update = update;
            return this;
        }

        public State OnEnter(Action enter)
        {
            Enter = enter;
            return this;
        }

        public State OnExit(Action exit)
        {
            Exit = exit;
            return this;
        }

        public State SetMinTime(float value)
        {
            MinTime = Mathf.Max(0f, value);
            return this;
        }

        public State SetTimeout(float value)
        {
            Timeout = value;
            return this;
        }

        public State SetRestart(T id)
        {
            RestartId = id;
            return this;
        }

        public State SetProcessMode(FSMProcessMode mode)
        {
            ProcessMode = mode;
            return this;
        }

        public State Lock(FSMLockMode mode = FSMLockMode.Full)
        {
            LockMode = mode;
            return this;
        }

        public State UnLock()
        {
            LockMode = FSMLockMode.None;
            return this;
        }

        public State AddTags(params string[] what)
        {
            foreach (string tag in what)
                tags.Add(tag);
            return this;
        }

        public State RegisterData(string id, object value)
        {
            if (data.ContainsKey(id))
            {
                GD.PushError($"Trying to add an existing data with Id: {id}");
                return this;
            }
            data[id] = value;
            return this;
        }

        public bool RemoveData(string id)
        {
            return data.Remove(id);
        }

        public bool TryGetData<TData>(string id, out TData value)
        {
            if (data.TryGetValue(id, out var obj) && obj is TData typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }

        public bool IsLocked() => LockMode != FSMLockMode.None;
        public bool IsFullyLocked() => LockMode == FSMLockMode.Full;
        public bool TransitionBlocked() => LockMode == FSMLockMode.Transition;

        public bool HasTag(string tag) => tags.Contains(tag);
        public bool HasData(string id) => data.ContainsKey(id);
        public bool HasData(object value) => data.ContainsValue(value);
    }

    public class Transition
    {
        private static int globalInsertionCounter = 0;

        public T From { get; private set; }
        public T To { get; private set; }
        public Predicate<StateMachine<T>> Condition { get; private set; }

        public float OverrideMinTime { get; private set; } = -1f;
        public int Priority { get; private set; }
        public int InsertionIndex { get; private set; }

        public bool ForceInstantTransition { get; private set; }

        public Action Triggered { get; private set; }

        public Transition OnTriggered(Action callback)
        {
            Triggered = callback;
            return this;
        }

        public Transition SetCondition(Predicate<StateMachine<T>> condition)
        {
            Condition = condition;
            return this;   
        }

        public Transition SetMinTime(float minTime)
        {
            OverrideMinTime = Mathf.Max(0f, minTime);
            return this;
        }

        public Transition SetPriority(int priority)
        {
            Priority = priority;
            return this;
        }

        public Transition SetOnTop()
        {
            Priority = int.MaxValue;
            return this;
        }

        public Transition ForceInstant()
        {
            ForceInstantTransition = true;
            return this;
        }

        public Transition BreakInstant()
        {
            ForceInstantTransition = false;
            return this;
        }

        public Transition(T from, T to)
        {
            From = from;
            To = to;
            InsertionIndex = globalInsertionCounter++;
        }

        internal static int Compare(Transition a, Transition b)
        {
            int priorityCompare = b.Priority.CompareTo(a.Priority);
            return priorityCompare != 0 ? priorityCompare : a.InsertionIndex.CompareTo(b.InsertionIndex);
        }
    }


}
