using System.Collections.Generic;

namespace Utilities.InputManagement;

public class BufferManager
{
    private readonly List<InputBuffer> buffers = new();

    public void BufferAction(string action, double duration)
    {
        InputBuffer existing = buffers.Find(b => b.Action == action);

        if (existing != null)
        {
            existing.SetDuration(duration);
            return;
        }
        InputBuffer buffer = new InputBuffer(action, duration);
        buffers.Add(buffer);
    }

    public bool HasAction(string action)
    {
        return buffers.Find(b => b.Action == action && b.IsValid) != null;
    }

    public bool TryConsume(string action)
    {
        InputBuffer inputBuffer = buffers.Find(b => b.Action == action && b.IsValid);

        if (inputBuffer == null)
            return false;
        buffers.Remove(inputBuffer);
        return true;
    }

    public void Update(double delta)
    {
        for (int i = buffers.Count - 1; i >= 0; i--)
        {
            buffers[i].Update(delta);

            if (!buffers[i].IsValid)
                buffers.RemoveAt(i);
        }
    }

    public class InputBuffer
    {
        public string Action { get; private set; }
        public double ExpireTime { get; private set; }

        public bool IsValid => ExpireTime > 0.0;

        public void SetDuration(double duration)
        {
            ExpireTime = duration;
        }

        public void Update(double delta)
        {
            ExpireTime -= delta;
        }

        public InputBuffer(string action, double duration)
        {
            Action = action;
            ExpireTime = duration;
        }
    }
}
