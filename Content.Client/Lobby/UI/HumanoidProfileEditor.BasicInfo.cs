
using Content.Shared.Preferences;

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
        Profile = Profile == null
            ? HumanoidCharacterProfile.Random()
            : HumanoidCharacterProfile.Random(RandomizeLockButton.RandomizeCfg, Profile!);
        SetProfile(Profile, CharacterSlot);
        SetDirty();
    }
}
