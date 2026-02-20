using System.Linq;
using KnobForge.Core;
using KnobForge.Core.Scene;

namespace KnobForge.Rendering.GPU;

public static class CollarMeshBuilder
{
    public static CollarMesh? TryBuildFromProject(KnobProject? project)
    {
        if (project is null)
        {
            return null;
        }

        ModelNode? modelNode = project.SceneRoot.Children
            .OfType<ModelNode>()
            .FirstOrDefault();
        if (modelNode is null)
        {
            return null;
        }

        CollarNode? collarNode = modelNode.Children
            .OfType<CollarNode>()
            .FirstOrDefault();
        if (collarNode is null || !collarNode.Enabled || collarNode.Preset == CollarPreset.None)
        {
            return null;
        }

        return collarNode.Preset switch
        {
            CollarPreset.SnakeOuroboros => OuroborosCollarMeshBuilder.TryBuildFromProject(project),
            CollarPreset.ImportedStl => ImportedStlCollarMeshBuilder.TryBuildFromProject(project),
            CollarPreset.MeshyOuroborosRing => ImportedStlCollarMeshBuilder.TryBuildFromProject(project),
            CollarPreset.MeshyOuroborosRingTextured => ImportedStlCollarMeshBuilder.TryBuildFromProject(project),
            _ => null
        };
    }
}
