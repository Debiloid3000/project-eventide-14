using Content.Shared.Preferences;
using Robust.Shared.Utility;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    private HumanoidCharacterProfile.RandomizeCfg _randomizeConfig = HumanoidCharacterProfile.RandomizeConfigAll;

    private void InitializeRandomizationControls()
    {
        RandomizeUnlockedButton.OnPressed += _ => RandomizeProfile();
        RandomizeNameButton.OnPressed += _ => RandomizeNameOnly();
        NameRandomizeSettingsButton.OnPressed += _ => ToggleRandomizationSettings();
        RandomizeSettingsButton.OnPressed += _ => ToggleRandomizationSettings();

        RandomizeFirstNameCheckBox.Pressed = IsRandomizeEnabled(HumanoidCharacterProfile.RandomizeCfg.FirstName);
        RandomizeLastNameCheckBox.Pressed = IsRandomizeEnabled(HumanoidCharacterProfile.RandomizeCfg.LastName);
        RandomizeFirstNameCheckBox.OnToggled += args =>
        {
            SetRandomizeFlag(HumanoidCharacterProfile.RandomizeCfg.FirstName, args.Pressed);
            UpdateNameRandomizationFlag();
        };
        RandomizeLastNameCheckBox.OnToggled += args =>
        {
            SetRandomizeFlag(HumanoidCharacterProfile.RandomizeCfg.LastName, args.Pressed);
            UpdateNameRandomizationFlag();
        };
        UpdateNameRandomizationFlag();

        RandomizeSpeciesCheckBox.Pressed = IsRandomizeEnabled(HumanoidCharacterProfile.RandomizeCfg.Species);
        RandomizeSpeciesCheckBox.OnToggled += args => SetRandomizeFlag(HumanoidCharacterProfile.RandomizeCfg.Species, args.Pressed);

        RandomizeAgeCheckBox.Pressed = IsRandomizeEnabled(HumanoidCharacterProfile.RandomizeCfg.Age);
        RandomizeAgeCheckBox.OnToggled += args => SetRandomizeFlag(HumanoidCharacterProfile.RandomizeCfg.Age, args.Pressed);

        RandomizeSexCheckBox.Pressed = IsRandomizeEnabled(HumanoidCharacterProfile.RandomizeCfg.Sex);
        RandomizeSexCheckBox.OnToggled += args => SetRandomizeFlag(HumanoidCharacterProfile.RandomizeCfg.Sex, args.Pressed);

        RandomizeGenderCheckBox.Pressed = IsRandomizeEnabled(HumanoidCharacterProfile.RandomizeCfg.Gender);
        RandomizeGenderCheckBox.OnToggled += args => SetRandomizeFlag(HumanoidCharacterProfile.RandomizeCfg.Gender, args.Pressed);

        RandomizeSkinCheckBox.Pressed = IsRandomizeEnabled(HumanoidCharacterProfile.RandomizeCfg.Skin);
        RandomizeSkinCheckBox.OnToggled += args => SetRandomizeFlag(HumanoidCharacterProfile.RandomizeCfg.Skin, args.Pressed);

        RandomizeEyesCheckBox.Pressed = IsRandomizeEnabled(HumanoidCharacterProfile.RandomizeCfg.Eyes);
        RandomizeEyesCheckBox.OnToggled += args => SetRandomizeFlag(HumanoidCharacterProfile.RandomizeCfg.Eyes, args.Pressed);

        RandomizeMarkingsCheckBox.Pressed = IsRandomizeEnabled(HumanoidCharacterProfile.RandomizeCfg.Markings);
        RandomizeMarkingsCheckBox.OnToggled += args => SetRandomizeFlag(HumanoidCharacterProfile.RandomizeCfg.Markings, args.Pressed);

        RandomizeJobsCheckBox.Pressed = IsRandomizeEnabled(HumanoidCharacterProfile.RandomizeCfg.Jobs);
        RandomizeJobsCheckBox.OnToggled += args => SetRandomizeFlag(HumanoidCharacterProfile.RandomizeCfg.Jobs, args.Pressed);

        RandomizeAntagsCheckBox.Pressed = IsRandomizeEnabled(HumanoidCharacterProfile.RandomizeCfg.Antags);
        RandomizeAntagsCheckBox.OnToggled += args => SetRandomizeFlag(HumanoidCharacterProfile.RandomizeCfg.Antags, args.Pressed);

        RandomizeTraitsCheckBox.Pressed = IsRandomizeEnabled(HumanoidCharacterProfile.RandomizeCfg.Traits);
        RandomizeTraitsCheckBox.OnToggled += args => SetRandomizeFlag(HumanoidCharacterProfile.RandomizeCfg.Traits, args.Pressed);

        RandomizeLoadoutsCheckBox.Pressed = IsRandomizeEnabled(HumanoidCharacterProfile.RandomizeCfg.Loadouts);
        RandomizeLoadoutsCheckBox.OnToggled += args => SetRandomizeFlag(HumanoidCharacterProfile.RandomizeCfg.Loadouts, args.Pressed);

        ConsiderClothingCheckBox.OnToggled += args =>
        {
            ClothingStyleButton.Visible = args.Pressed;
            UniformStyleButton.Visible = args.Pressed;
        };

        ClothingStyleButton.AddItem(Loc.GetString("humanoid-profile-editor-randomizer-clothing-any"), 0);
        ClothingStyleButton.AddItem(Loc.GetString("humanoid-profile-editor-randomizer-clothing-male"), 1);
        ClothingStyleButton.AddItem(Loc.GetString("humanoid-profile-editor-randomizer-clothing-female"), 2);
        ClothingStyleButton.SelectId(0);

        UniformStyleButton.AddItem(Loc.GetString("humanoid-profile-editor-randomizer-uniform-any"), 0);
        UniformStyleButton.AddItem(Loc.GetString("humanoid-profile-editor-randomizer-uniform-jumpsuit"), 1);
        UniformStyleButton.AddItem(Loc.GetString("humanoid-profile-editor-randomizer-uniform-jumpskirt"), 2);
        UniformStyleButton.SelectId(0);
    }

    private bool IsRandomizeEnabled(HumanoidCharacterProfile.RandomizeCfg flag)
    {
        return (_randomizeConfig & flag) != 0;
    }

    private void SetRandomizeFlag(HumanoidCharacterProfile.RandomizeCfg flag, bool enabled)
    {
        if (enabled)
            _randomizeConfig |= flag;
        else
            _randomizeConfig &= ~flag;
    }

    private void UpdateNameRandomizationFlag()
    {
        var randomizeName = IsRandomizeEnabled(HumanoidCharacterProfile.RandomizeCfg.FirstName) ||
                            IsRandomizeEnabled(HumanoidCharacterProfile.RandomizeCfg.LastName);
        SetRandomizeFlag(HumanoidCharacterProfile.RandomizeCfg.Name, randomizeName);
    }
}
