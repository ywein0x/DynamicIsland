namespace DynamicIsland;

/// <summary>
/// Damped spring used for the iOS-style fluid motion of the island.
/// Slightly under-damped so size changes settle with a soft bounce.
/// </summary>
sealed class Spring(double value)
{
    public double Value = value;
    public double Target = value;
    public double Velocity;
    public double Stiffness = 280;
    public double Damping = 26;

    public bool AtRest => Math.Abs(Value - Target) < 0.01 && Math.Abs(Velocity) < 0.01;

    public void Step(double dt)
    {
        // Fixed sub-steps keep the integration stable at low frame rates.
        const double subStep = 1 / 240.0;
        while (dt > 0)
        {
            var step = Math.Min(dt, subStep);
            var accel = -Stiffness * (Value - Target) - Damping * Velocity;
            Velocity += accel * step;
            Value += Velocity * step;
            dt -= step;
        }

        if (AtRest)
        {
            Value = Target;
            Velocity = 0;
        }
    }
}
