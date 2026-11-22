
using Content.Shared.Preferences;
using Robust.Shared.Random;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    private void SetName(string newName)
    {
        Profile = Profile?.WithName(newName);
        SetDirty();

        if (!IsDirty)
            return;

        SpriteView.SetName(newName);
    }

    private void UpdateNameEdit()
    {
        NameEdit.Text = Profile?.Name ?? "";
    }

    private void RandomizeNameOnly()
    {
        if (Profile is null)
            return;

        var config = RandomizeLockButton.RandomizeCfg;
        if ((config & HumanoidCharacterProfile.RandomizeCfg.Name) == 0)
            return;

        var randomizeFirstName = (config & HumanoidCharacterProfile.RandomizeCfg.FirstName) != 0;
        var randomizeLastName = (config & HumanoidCharacterProfile.RandomizeCfg.LastName) != 0;

        Profile = Profile.RandomizeName(randomizeFirstName, randomizeLastName);
        UpdateNameEdit();
        ReloadPreview();
        SetDirty();
    }

    private void ToggleRandomizationSettings()
    {
        RandomizationSettingsPanel.Visible = !RandomizationSettingsPanel.Visible;
    }

    /// <summary>
    /// Randomize values selectively while respecting locked values.
    /// </summary>
    private void RandomizeProfile()
    {
        var config = RandomizeLockButton.RandomizeCfg;
        Profile = Profile == null
            ? HumanoidCharacterProfile.Random()
            : HumanoidCharacterProfile.Random(config, Profile);

        if (Profile == null)
            return;

        var random = IoCManager.Resolve<IRobustRandom>();

        if ((config & HumanoidCharacterProfile.RandomizeCfg.Jobs) != 0)
            RandomizeJobPreferences(random);

        if ((config & HumanoidCharacterProfile.RandomizeCfg.Antags) != 0)
            RandomizeAntagPreferences(random);

        if ((config & HumanoidCharacterProfile.RandomizeCfg.Traits) != 0)
            RandomizeTraitPreferences(random);

        if ((config & HumanoidCharacterProfile.RandomizeCfg.Loadouts) != 0)
        {
            RandomizeRoleLoadouts(
                ConsiderClothingCheckBox.Pressed,
                ClothingStyleButton.SelectedId == 1,
                random);
        }

        SetProfile(Profile, CharacterSlot);
        SetDirty();
    }
}
