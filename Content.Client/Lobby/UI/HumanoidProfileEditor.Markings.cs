using Content.Shared.Humanoid;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    private void UpdateMarkings()
    {
        if (Profile == null)
        {
            return;
        }

        _markingsModel.OrganData = _markingManager.GetMarkingData(Profile.Species);
        _markingsModel.OrganProfileData = _markingManager.GetProfileData(Profile.Species, Profile.Sex, Profile.Appearance.SkinColor, Profile.Appearance.EyeColor);
        _markingsModel.Markings = Profile.Appearance.Markings;
        UpdateRandomizerOptions();
    }

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

    private void OnMarkingChange()
    {
        if (Profile is null)
            return;

        Profile = Profile.WithCharacterAppearance(Profile.Appearance.WithMarkings(_markingsModel.Markings));
        ReloadProfilePreview();
        SetDirty();
    }
}
