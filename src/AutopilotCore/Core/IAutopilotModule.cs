namespace AutopilotCore
{
    public interface IAutopilotModule
    {
        string Name { get; }
        bool Enabled { get; set; }

        // Mode and target, short enough for a collapsed section header.
        string Summary { get; }

        void Update(Vessel vessel, FlightCtrlState ctrl);
        void DrawGui();
    }
}
