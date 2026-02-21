using Avalonia.Threading;
using KnobForge.App.Controls;

namespace KnobForge.App.Views
{
    public partial class MainWindow
    {
        private void OnViewportRuntimeDiagnosticsUpdated(MetalViewport.RuntimeDiagnosticsSnapshot snapshot)
        {
            if (_runtimeDiagnosticsText == null)
            {
                return;
            }

            void Apply()
            {
                string hitLabel = snapshot.LastHitMode switch
                {
                    MetalViewport.PaintHitMode.MeshHit => "hit:mesh",
                    MetalViewport.PaintHitMode.Fallback => "hit:fallback",
                    _ => "hit:idle"
                };
                string paintState = snapshot.IsPainting ? "painting" : "idle";
                _runtimeDiagnosticsText.Text =
                    $"fps {snapshot.SmoothedFps:0.0} | frame {snapshot.SmoothedCpuFrameMs:0.00} ms | paint {snapshot.PaintStampCpuMs:0.00} ms | queue {snapshot.PendingPaintStamps} | {hitLabel} | {paintState}";
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                Apply();
            }
            else
            {
                Dispatcher.UIThread.Post(Apply, DispatcherPriority.Background);
            }
        }
    }
}
