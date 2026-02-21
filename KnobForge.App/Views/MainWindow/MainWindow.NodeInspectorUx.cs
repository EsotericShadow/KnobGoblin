using KnobForge.Core.Scene;

namespace KnobForge.App.Views
{
    public partial class MainWindow
    {
        private void UpdateNodeInspectorForSelection(SceneNode? selectedNode)
        {
            if (_nodeInspectorContextText != null)
            {
                _nodeInspectorContextText.Text = selectedNode switch
                {
                    ModelNode => "Selected: Knob Model",
                    CollarNode => "Selected: Collar (collar controls focused)",
                    MaterialNode => "Selected: Material (material controls focused)",
                    LightNode => "Selected: Light (use Lighting tab)",
                    SceneRootNode => "Selected: Scene Root",
                    _ => "Selected: Node"
                };
            }

            UpdateContextStrip(selectedNode);
        }
    }
}
