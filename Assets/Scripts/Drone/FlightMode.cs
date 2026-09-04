namespace Sim.Drone
{
    /// <summary>
    /// The three flight modes the drone can fly in. Yaw is rate-controlled in all
    /// three (see DroneFlightModel) — only pitch/roll behaviour changes between them.
    /// </summary>
    public enum FlightMode
    {
        /// <summary>Pitch/roll hold a target angle proportional to stick deflection and self-level when centered.</summary>
        Angle,

        /// <summary>Pitch/roll are open-loop angular rates proportional to stick deflection. No self-leveling.</summary>
        Acro,

        /// <summary>Self-levels near center stick like Angle mode, blends toward Acro-mode rates at full deflection.</summary>
        Horizon
    }
}
