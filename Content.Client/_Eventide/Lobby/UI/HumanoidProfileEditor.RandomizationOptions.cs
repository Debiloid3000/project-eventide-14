using System.Linq;
using Content.Shared.Humanoid;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    private void UpdateRandomizerOptions()
    {
        var layers = _markingsModel.OrganData.Values
            .SelectMany(data => data.Layers)
            .ToHashSet();

        var hasHair = layers.Contains(HumanoidVisualLayers.Hair);
        var hasFacialHair = layers.Contains(HumanoidVisualLayers.FacialHair);
        var hasHeadTop = layers.Contains(HumanoidVisualLayers.HeadTop);
        var hasTail = layers.Contains(HumanoidVisualLayers.Tail);
        var hasBodyMarkings = layers.Any(layer =>
            layer != HumanoidVisualLayers.Hair &&
            layer != HumanoidVisualLayers.FacialHair &&
            layer != HumanoidVisualLayers.HeadTop &&
            layer != HumanoidVisualLayers.Tail);

        RandomizeHairStyleLock.Visible = hasHair;
        RandomizeHairColorLock.Visible = hasHair;
        RandomizeFacialHairStyleLock.Visible = hasFacialHair;
        RandomizeFacialHairColorLock.Visible = hasFacialHair;
        RandomizeHeadTopStyleLock.Visible = hasHeadTop;
        RandomizeHeadTopColorLock.Visible = hasHeadTop;
        RandomizeTailStyleLock.Visible = hasTail;
        RandomizeTailColorLock.Visible = hasTail;
        RandomizeBodyMarkingsStyleLock.Visible = hasBodyMarkings;
        RandomizeBodyMarkingsColorLock.Visible = hasBodyMarkings;
    }

}
