#nullable enable
using System;

namespace Editor.Analysis.Models.Blocks;

/// <summary>
/// Runtime execution telemetry, frequency counters, and thermal heat monitoring.
/// </summary>
public class TelemetryActivity
{
    /// <summary> Total lifetime invocations count. </summary>
    public long InvocationCount { get; set; }

    /// <summary> Dynamic calls frequency per second. </summary>
    public double CallsPerSecond { get; set; }

    /// <summary> Total accumulated execution time in milliseconds this frame. </summary>
    public double FrameExecutionTimeMs { get; set; }

    /// <summary> Average execution duration per call in milliseconds. </summary>
    public double AverageDurationMs { get; set; }

    /// <summary> Highest recorded execution duration in milliseconds. </summary>
    public double PeakDurationMs { get; set; }

    /// <summary>
    /// Normalized thermal heat level [0.0 (Cold/Idle) to 1.0 (White Hot)].
    /// </summary>
    public float HeatLevel { get; set; }

    /// <summary>
    /// Updates thermal dissipation via Continuous Exponential Moving Average (EMA).
    /// </summary>
    public void UpdateDecay( float deltaTime, float coolingRate = 2.5f )
    {
        float decayedHeat = HeatLevel * MathF.Exp( -coolingRate * deltaTime );
        float heatAdded = (float)(1.0 - Math.Exp( -0.5 * FrameExecutionTimeMs ));
        HeatLevel = Math.Clamp( decayedHeat + heatAdded, 0.0f, 1.0f );
        FrameExecutionTimeMs = 0; // Reset frame accumulation
    }
}