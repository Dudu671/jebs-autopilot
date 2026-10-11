namespace AutopilotCore
{
    public interface IAutopilotModule
    {
        string Name { get; }
        bool Enabled { get; set; }
        void Update(Vessel vessel, FlightCtrlState ctrl);
        void DrawGui();
    }
}
