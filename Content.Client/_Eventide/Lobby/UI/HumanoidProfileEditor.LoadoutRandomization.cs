using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.Clothing;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Robust.Shared;
using Robust.Shared.Random;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    private enum UniformRandomizationStyle
    {
        Any,
        Jumpsuit,
        Jumpskirt
    }

    /// <summary>
    /// Randomizes wearable clothing for preferred jobs and selected antagonist roles.
    /// Uniform style and the style of all other clothing slots are deliberately independent.
    /// Non-clothing equipment groups are never changed by this option.
    /// </summary>
    private void RandomizeRoleLoadouts(
        bool considerClothing,
        LoadoutClothingStyle clothingStyle,
        UniformRandomizationStyle uniformStyle,
        IRobustRandom random)
    {
        if (!considerClothing ||
            Profile is null ||
            _playerManager.LocalSession is not { } session ||
            IoCManager.Instance is not { } collection)
            return;

        var roleIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (jobId, priority) in Profile.JobPriorities)
        {
            if (priority == JobPriority.Never || !_prototypeManager.TryIndex(jobId, out var job))
                continue;

            roleIds.Add(LoadoutSystem.GetJobPrototype(job.ID));
        }

        foreach (var antag in Profile.AntagPreferences)
            roleIds.Add(antag.Id);

        foreach (var roleId in roleIds)
        {
            if (!_prototypeManager.TryIndex<RoleLoadoutPrototype>(roleId, out var roleProto) ||
                roleProto.Groups.Count == 0)
                continue;

            var loadout = Profile.Loadouts.TryGetValue(roleId, out var existing)
                ? existing.Clone()
                : new RoleLoadout(roleProto.ID);

            loadout.SetDefault(Profile, session, _prototypeManager);

            foreach (var groupId in roleProto.Groups)
            {
                if (!_prototypeManager.TryIndex(groupId, out var groupProto) ||
                    groupProto.Hidden ||
                    !IsClothingLoadoutGroup(groupId.Id))
                    continue;

                var candidates = groupProto.Loadouts.ToList();
                if (candidates.Count == 0)
                    continue;

                if (IsUniformLoadoutGroup(groupId.Id))
                {
                    var uniformCandidates = uniformStyle switch
                    {
                        UniformRandomizationStyle.Jumpsuit => candidates
                            .Where(candidate => !IsSkirtOrDress(candidate.Id))
                            .ToList(),
                        UniformRandomizationStyle.Jumpskirt => candidates
                            .Where(candidate => IsSkirtOrDress(candidate.Id))
                            .ToList(),
                        _ => candidates
                    };

                    if (uniformCandidates.Count > 0)
                        candidates = uniformCandidates;
                }
                else if (clothingStyle != LoadoutClothingStyle.Any)
                {
                    var styleCandidates = candidates
                        .Where(candidate =>
                        {
                            var style = _prototypeManager.Index(candidate).ClothingStyle;
                            return style == LoadoutClothingStyle.Any || style == clothingStyle;
                        })
                        .ToList();

                    if (styleCandidates.Count > 0)
                        candidates = styleCandidates;
                }

                if (!loadout.SelectedLoadouts.TryGetValue(groupId, out var selectedLoadouts))
                {
                    selectedLoadouts = new List<Loadout>();
                    loadout.SelectedLoadouts[groupId] = selectedLoadouts;
                }

                selectedLoadouts.Clear();
                loadout.AddLoadout(groupId, random.Pick(candidates), _prototypeManager);
            }

            loadout.EnsureValid(Profile, session, collection);
            Profile = Profile.WithLoadout(loadout);
        }
    }

    private static bool IsUniformLoadoutGroup(string groupId)
    {
        return groupId.Contains("Jumpsuit", StringComparison.OrdinalIgnoreCase) ||
               groupId.Contains("Uniform", StringComparison.OrdinalIgnoreCase) ||
               groupId.Contains("Skirt", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSkirtOrDress(string loadoutId)
    {
        return loadoutId.Contains("skirt", StringComparison.OrdinalIgnoreCase) ||
               loadoutId.Contains("dress", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsClothingLoadoutGroup(string groupId)
    {
        string[] clothingSlots =
        [
            "Jumpsuit", "Uniform", "OuterClothing", "Head", "Neck", "Shoes",
            "Gloves", "Mask", "Glasses", "Ears", "Belt", "Wrist"
        ];

        return clothingSlots.Any(slot =>
            groupId.Contains(slot, StringComparison.OrdinalIgnoreCase));
    }
}
