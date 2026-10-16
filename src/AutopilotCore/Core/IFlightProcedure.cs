namespace AutopilotCore
{
    // Modes that fly a whole procedure, every axis at once, and annunciate their own modes.
    public interface IFlightProcedure : ILateralMode, IVerticalMode, IThrottleMode
    {
        string LateralLabel { get; }
        string LateralValue { get; }
        string VerticalLabel { get; }
        float SpeedTargetKt { get; }

        // While true the fly-by-wire yaw damper must leave the rudder alone.
        bool OwnsRudder { get; }

        bool TryGetDesiredTrack(out float track);
    }
}
