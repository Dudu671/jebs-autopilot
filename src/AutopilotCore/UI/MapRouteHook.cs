using UnityEngine;

namespace AutopilotCore
{
    // Screen-space route on the map camera. The planet image is composited before OnGUI,
    // which is why labels show up there and a world-space line drawn in OnPostRender does not.
    public sealed class MapRouteHook : MonoBehaviour
    {
        public MapRouteOverlay Overlay;

        private void OnGUI()
        {
            if (Overlay != null)
            {
                Overlay.DrawGui(GetComponent<Camera>());
            }
        }
    }
}
