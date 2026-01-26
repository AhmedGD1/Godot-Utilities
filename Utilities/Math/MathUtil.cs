using Godot;
using System.Collections.Generic;

namespace Utilities.Math;

/// <summary>
/// Math utilities that complement Godot's built-in Mathf.
/// Use Godot's built-in for: Remap, Wrap, Snap, LerpAngle, MoveToward, etc.
/// </summary>
public static class MathUtil
{
    public static RandomNumberGenerator RNG { get; private set; } = new();

    static MathUtil()
    {
        RNG.Randomize();
    }

    public static void SeedRNG(ulong seed) => RNG = new RandomNumberGenerator { Seed = seed };

    #region Framerate-Independent Smoothing

    /// <summary>
    /// Framerate-independent lerp. Use instead of Lerp(a, b, speed * delta).
    /// </summary>
    public static float DeltaLerp(float a, float b, float deltaTime, float smoothing) 
    {
        float t = 1f - Mathf.Exp(-deltaTime * smoothing);
        return Mathf.Lerp(a, b, t);
    }

    public static Vector2 DeltaLerp(Vector2 a, Vector2 b, float deltaTime, float smoothing)
    {
        float t = 1f - Mathf.Exp(-deltaTime * smoothing);
        return a.Lerp(b, t);
    }

    /// <summary>
    /// Unity-style smooth damping with velocity. Better than lerp for camera following.
    /// </summary>
    public static float SmoothDamp(float current, float target, ref float velocity, 
        float smoothTime, float deltaTime, float maxSpeed = Mathf.Inf)
    {
        smoothTime = Mathf.Max(0.0001f, smoothTime);
        float omega = 2f / smoothTime;
        float x = omega * deltaTime;
        float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
        
        float change = current - target;
        float maxChange = maxSpeed * smoothTime;
        change = Mathf.Clamp(change, -maxChange, maxChange);
        
        float temp = (velocity + omega * change) * deltaTime;
        velocity = (velocity - omega * temp) * exp;
        
        return target + (change + temp) * exp;
    }

    public static Vector2 SmoothDamp(Vector2 current, Vector2 target, ref Vector2 velocity, float smoothTime, float deltaTime)
    {
        return new Vector2(
            SmoothDamp(current.X, target.X, ref velocity.X, smoothTime, deltaTime),
            SmoothDamp(current.Y, target.Y, ref velocity.Y, smoothTime, deltaTime)
        );
    }

    #endregion

    #region Random Utilities

    /// <summary>Random chance check. Cleaner than RNG.Randf() &lt; probability.</summary>
    public static bool Chance(float probability) => RNG.Randf() < probability;
    
    /// <summary>Choose random item from array.</summary>
    public static T Choose<T>(params T[] items) => items[RNG.RandiRange(0, items.Length - 1)];
    
    /// <summary>Choose random item from list.</summary>
    public static T Choose<T>(List<T> items) => items[RNG.RandiRange(0, items.Count - 1)];
    
    /// <summary>Fisher-Yates shuffle.</summary>
    public static void Shuffle<T>(T[] array)
    {
        for (int i = array.Length - 1; i > 0; i--)
        {
            int j = RNG.RandiRange(0, i);
            (array[i], array[j]) = (array[j], array[i]);
        }
    }

    /// <summary>
    /// Random point inside circle with uniform distribution.
    /// Note: Don't use RNG.RandfRange for radius - it creates clustering at center!
    /// </summary>
    public static Vector2 RandomInCircle(float radius)
    {
        float angle = RNG.Randf() * Mathf.Tau;
        float r = Mathf.Sqrt(RNG.Randf()) * radius;
        return Vector2.FromAngle(angle) * r;
    }

    /// <summary>Random point on circle edge.</summary>
    public static Vector2 RandomOnCircle(float radius)
    {
        return Vector2.FromAngle(RNG.Randf() * Mathf.Tau) * radius;
    }

    #endregion

    #region Intersection Helpers

    /// <summary>Check if two circles overlap.</summary>
    public static bool CircleOverlap(Vector2 center1, float radius1, Vector2 center2, float radius2)
    {
        float radiusSum = radius1 + radius2;
        return center1.DistanceSquaredTo(center2) <= radiusSum * radiusSum;
    }

    /// <summary>Check if point is inside circle.</summary>
    public static bool PointInCircle(Vector2 point, Vector2 center, float radius)
    {
        return point.DistanceSquaredTo(center) <= radius * radius;
    }

    /// <summary>Closest point on line segment to given point.</summary>
    public static Vector2 ClosestPointOnLine(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
    {
        Vector2 line = lineEnd - lineStart;
        float len = line.LengthSquared();
        if (len == 0f) return lineStart;
        
        float t = Mathf.Clamp((point - lineStart).Dot(line) / len, 0f, 1f);
        return lineStart + line * t;
    }

    #endregion

    #region Easing Functions
    
    /// <summary>
    /// Standalone easing functions. For tweening, use GTween instead.
    /// For manual animations/calculations where you need the easing curve.
    /// </summary>
    public static class Ease
    {
        public static float InOutQuad(float t) 
            => t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
        
        public static float OutQuad(float t) 
            => 1f - (1f - t) * (1f - t);
        
        public static float InOutCubic(float t)
            => t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
        
        public static float OutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }
        
        public static float OutElastic(float t)
        {
            if (t == 0f || t == 1f) return t;
            const float c4 = Mathf.Tau / 3f;
            return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * c4) + 1f;
        }
        
        public static float OutBounce(float t)
        {
            const float n1 = 7.5625f;
            const float d1 = 2.75f;
            
            if (t < 1f / d1) return n1 * t * t;
            if (t < 2f / d1) { t -= 1.5f / d1; return n1 * t * t + 0.75f; }
            if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
            t -= 2.625f / d1;
            return n1 * t * t + 0.984375f;
        }
    }

    #endregion
}