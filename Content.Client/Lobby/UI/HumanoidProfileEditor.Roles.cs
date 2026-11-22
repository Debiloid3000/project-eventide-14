using System.Linq;
using System.Numerics;
using Content.Client.Lobby.UI.Loadouts;
using Content.Client.Lobby.UI.Roles;
using Content.Shared.Antag;
using Content.Shared.Clothing;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Content.Shared.Traits;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{

    /// <summary>
    /// Temporary override of their selected job, used to preview roles.
    /// </summary>
    public JobPrototype? JobOverride;

    // One at a time.
    private LoadoutWindow? _loadoutWindow;

    // Genesis-Edit: track only the antagonist loadout currently open in the editor preview.
    private RoleLoadout? _previewRoleLoadout;
    private RoleLoadoutPrototype? _previewRoleLoadoutPrototype;

    private List<(string, RequirementsSelector)> _jobPriorities = new();

    private readonly Dictionary<string, BoxContainer> _jobCategories;

    /// <summary>
    /// Updates selected job priorities to the profile's.
    /// </summary>
    private void UpdateJobPriorities()
    {
        foreach (var (jobId, prioritySelector) in _jobPriorities)
        {
            var priority = Profile?.JobPriorities.GetValueOrDefault(jobId, JobPriority.Never) ?? JobPriority.Never;
            prioritySelector.Select((int)priority);
        }
    }

    /// <summary>
    /// Refresh all loadouts.
    /// </summary>
    public void RefreshLoadouts()
    {
        _loadoutWindow?.Dispose();
    }

    // Genesis-Start: Allow the same loadout editor to serve job and antagonist preferences.
    private void OpenLoadout(JobPrototype? jobProto, RoleLoadout roleLoadout, RoleLoadoutPrototype roleLoadoutProto, string? roleName = null)
    // Genesis-End
    {
        _loadoutWindow?.Dispose();
        _loadoutWindow = null;
        var collection = IoCManager.Instance;

        if (collection == null || _playerManager.LocalSession == null || Profile == null)
            return;

        JobOverride = jobProto;
        var session = _playerManager.LocalSession;

        _loadoutWindow = new LoadoutWindow(Profile, roleLoadout, roleLoadoutProto, _playerManager.LocalSession, collection)
        {
            // Genesis-Edit: Use the antag's localized name when the window is opened outside a job row.
            Title = Loc.GetString("loadout-window-title-loadout", ("job", roleName ?? jobProto?.LocalizedName ?? string.Empty)),
        };

        // Refresh the buttons etc.
        _loadoutWindow.RefreshLoadouts(roleLoadout, session, collection);
        _loadoutWindow.OpenCenteredLeft();

        _loadoutWindow.OnNameChanged += name =>
        {
            roleLoadout.EntityName = name;
            Profile = Profile.WithLoadout(roleLoadout);
            SetDirty();
        };

        _loadoutWindow.OnLoadoutPressed += (loadoutGroup, loadoutProto) =>
        {
            roleLoadout.AddLoadout(loadoutGroup, loadoutProto, _prototypeManager);
            _loadoutWindow.RefreshLoadouts(roleLoadout, session, collection);
            Profile = Profile?.WithLoadout(roleLoadout);
            // Genesis-Edit: Persist changed job/antagonist loadouts through the character editor's save flow.
            SetDirty();
            ReloadPreview();
        };

        _loadoutWindow.OnLoadoutUnpressed += (loadoutGroup, loadoutProto) =>
        {
            roleLoadout.RemoveLoadout(loadoutGroup, loadoutProto, _prototypeManager);
            _loadoutWindow.RefreshLoadouts(roleLoadout, session, collection);
            Profile = Profile?.WithLoadout(roleLoadout);
            // Genesis-Edit: Persist changed job/antagonist loadouts through the character editor's save flow.
            SetDirty();
            ReloadPreview();
        };

        JobOverride = jobProto;
        _previewRoleLoadout = jobProto == null ? roleLoadout : null;
        _previewRoleLoadoutPrototype = jobProto == null ? roleLoadoutProto : null;
        ReloadPreview();

        _loadoutWindow.OnClose += () =>
        {
            JobOverride = null;
            _previewRoleLoadout = null;
            _previewRoleLoadoutPrototype = null;
            ReloadPreview();
        };

        if (Profile is null)
            return;

        UpdateJobPriorities();
    }

    private static bool HasConfigurableLoadout(RoleLoadoutPrototype roleLoadoutProto)
    {
        return roleLoadoutProto.Groups.Count > 0 || roleLoadoutProto.CanCustomizeName;
    }

    /// <summary>
    /// Refreshes all job selectors.
    /// </summary>
    public void RefreshJobs()
    {
        JobList.RemoveAllChildren();
        _jobCategories.Clear();
        _jobPriorities.Clear();
        var firstCategory = true;

        // Get all displayed departments
        var departments = new List<DepartmentPrototype>();
        foreach (var department in _prototypeManager.EnumeratePrototypes<DepartmentPrototype>())
        {
            if (department.EditorHidden)
                continue;

            departments.Add(department);
        }

        departments.Sort(DepartmentUIComparer.Instance);

        var items = new[]
        {
                ("humanoid-profile-editor-job-priority-never-button", (int) JobPriority.Never),
                ("humanoid-profile-editor-job-priority-low-button", (int) JobPriority.Low),
                ("humanoid-profile-editor-job-priority-medium-button", (int) JobPriority.Medium),
                ("humanoid-profile-editor-job-priority-high-button", (int) JobPriority.High),
            };

        foreach (var department in departments)
        {
            var departmentName = Loc.GetString(department.Name);

            if (!_jobCategories.TryGetValue(department.ID, out var category))
            {
                category = new BoxContainer
                {
                    Orientation = LayoutOrientation.Vertical,
                    Name = department.ID,
                    ToolTip = Loc.GetString("humanoid-profile-editor-jobs-amount-in-department-tooltip",
                        ("departmentName", departmentName))
                };

                if (firstCategory)
                {
                    firstCategory = false;
                }
                else
                {
                    category.AddChild(new Control
                    {
                        MinSize = new Vector2(0, 23),
                    });
                }

                category.AddChild(new PanelContainer
                {
                    PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#232323") },
                    Children =
                        {
                            new Label
                            {
                                Text = Loc.GetString("humanoid-profile-editor-department-jobs-label",
                                    ("departmentName", departmentName)),
                                Margin = new Thickness(5f, 0, 0, 0)
                            }
                        }
                });

                _jobCategories[department.ID] = category;
                JobList.AddChild(category);
            }

            var jobs = department.Roles.Select(jobId => _prototypeManager.Index(jobId))
                .Where(job => job.SetPreference)
                .ToArray();

            if (JobUIComparer.TryCreate(_prototypeManager, null, out var comparer))
                Array.Sort(jobs, comparer);

            foreach (var job in jobs)
            {
                var jobContainer = new BoxContainer()
                {
                    Orientation = LayoutOrientation.Horizontal,
                };

                var selector = new RequirementsSelector()
                {
                    Margin = new Thickness(3f, 3f, 3f, 0f),
                };
                selector.OnOpenGuidebook += OnOpenGuidebook;

                var icon = new TextureRect
                {
                    TextureScale = new Vector2(2, 2),
                    VerticalAlignment = VAlignment.Center
                };
                var jobIcon = _prototypeManager.Index(job.Icon);
                icon.Texture = _sprite.Frame0(jobIcon.Icon);
                selector.Setup(items, job.LocalizedName, 200, job.LocalizedDescription, icon, job.Guides);

                if (!_requirements.IsAllowed(job, (HumanoidCharacterProfile?)_preferencesManager.Preferences?.SelectedCharacter, out var reason))
                {
                    selector.LockRequirements(reason);
                }
                else
                {
                    selector.UnlockRequirements();
                }

                selector.OnSelected += selectedPrio =>
                {
                    var selectedJobPrio = (JobPriority)selectedPrio;
                    Profile = Profile?.WithJobPriority(job.ID, selectedJobPrio);

                    foreach (var (jobId, other) in _jobPriorities)
                    {
                        // Sync other selectors with the same job in case of multiple department jobs
                        if (jobId == job.ID)
                        {
                            other.Select(selectedPrio);
                            continue;
                        }

                        if (selectedJobPrio != JobPriority.High || (JobPriority)other.Selected != JobPriority.High)
                            continue;

                        // Lower any other high priorities to medium.
                        other.Select((int)JobPriority.Medium);
                        Profile = Profile?.WithJobPriority(jobId, JobPriority.Medium);
                    }

                    // TODO: Only reload on high change (either to or from).
                    ReloadPreview();

                    UpdateJobPriorities();
                    SetDirty();
                };

                var loadoutWindowBtn = new Button()
                {
                    Text = Loc.GetString("loadout-window"),
                    HorizontalAlignment = HAlignment.Right,
                    VerticalAlignment = VAlignment.Center,
                    Margin = new Thickness(3f, 3f, 0f, 0f),
                    Disabled = true,
                };

                var collection = IoCManager.Instance!;
                var protoManager = collection.Resolve<IPrototypeManager>();

                // Keep the button in every profession row, but only enable a real editor.
                if (protoManager.TryIndex<RoleLoadoutPrototype>(
                        LoadoutSystem.GetJobPrototype(job.ID), out var roleLoadoutProto)
                    && HasConfigurableLoadout(roleLoadoutProto))
                {
                    loadoutWindowBtn.Disabled = false;
                    loadoutWindowBtn.OnPressed += _ =>
                    {
                        RoleLoadout? loadout = null;

                        // Clone so we don't modify the underlying loadout.
                        Profile?.Loadouts.TryGetValue(LoadoutSystem.GetJobPrototype(job.ID), out loadout);
                        loadout = loadout?.Clone();

                        if (loadout == null)
                        {
                            loadout = new RoleLoadout(roleLoadoutProto.ID);
                            loadout.SetDefault(Profile, _playerManager.LocalSession, _prototypeManager);
                        }

                        OpenLoadout(job, loadout, roleLoadoutProto);
                    };
                }

                _jobPriorities.Add((job.ID, selector));
                jobContainer.AddChild(selector);
                jobContainer.AddChild(loadoutWindowBtn);
                category.AddChild(jobContainer);
            }
        }

        UpdateJobPriorities();
    }

    public void RefreshAntags()
    {
        AntagList.RemoveAllChildren();

        // Only show loadout controls for roleLoadouts actually referenced by an antagonist specifier.
        var supportedAntagLoadouts = _prototypeManager.EnumeratePrototypes<AntagSpecifierPrototype>()
            .Where(specifier => specifier.RoleLoadout != null)
            .SelectMany(specifier => specifier.RoleLoadout!)
            .Select(roleLoadout => roleLoadout.Id)
            .ToHashSet();
        var items = new[]
        {
            ("humanoid-profile-editor-antag-preference-yes-button", 0),
            ("humanoid-profile-editor-antag-preference-no-button", 1)
        };

        foreach (var antag in _prototypeManager.EnumeratePrototypes<AntagPrototype>().OrderBy(a => Loc.GetString(a.Name)))
        {
            if (!antag.SetPreference)
                continue;

            var antagContainer = new BoxContainer()
            {
                Orientation = LayoutOrientation.Horizontal,
            };

            var selector = new RequirementsSelector()
            {
                Margin = new Thickness(3f, 3f, 3f, 0f),
            };
            selector.OnOpenGuidebook += OnOpenGuidebook;

            var title = Loc.GetString(antag.Name);
            var description = Loc.GetString(antag.Objective);
            selector.Setup(items, title, 250, description, guides: antag.Guides);
            selector.Select(Profile?.AntagPreferences.Contains(antag.ID) == true ? 0 : 1);

            if (!_requirements.IsAllowed(
                    antag,
                    (HumanoidCharacterProfile?)_preferencesManager.Preferences?.SelectedCharacter,
                    out var reason))
            {
                selector.LockRequirements(reason);
                Profile = Profile?.WithAntagPreference(antag.ID, false);
                SetDirty();
            }
            else
            {
                selector.UnlockRequirements();
            }

            selector.OnSelected += preference =>
            {
                Profile = Profile?.WithAntagPreference(antag.ID, preference == 0);
                SetDirty();
            };

            antagContainer.AddChild(selector);

            // Keep the loadout control visible for every antagonist preference.
            // Roles without a matching configurable RoleLoadoutPrototype stay disabled.
            var antagLoadoutButton = new Button()
            {
                Text = Loc.GetString("loadout-window"),
                HorizontalAlignment = HAlignment.Right,
                Margin = new Thickness(3f, 0f, 0f, 0f),
                Disabled = true,
            };

            if (supportedAntagLoadouts.Contains(antag.ID)
                && _prototypeManager.TryIndex<RoleLoadoutPrototype>(antag.ID, out var antagRoleLoadoutProto)
                && HasConfigurableLoadout(antagRoleLoadoutProto))
            {
                antagLoadoutButton.Disabled = false;
                antagLoadoutButton.OnPressed += _ =>
                {
                    if (Profile is null || _playerManager.LocalSession is not { } session)
                        return;

                    Profile.Loadouts.TryGetValue(antagRoleLoadoutProto.ID, out var loadout);
                    loadout = loadout?.Clone();

                    if (loadout is null)
                    {
                        loadout = new RoleLoadout(antagRoleLoadoutProto.ID);
                        loadout.SetDefault(Profile, session, _prototypeManager);
                    }

                    OpenLoadout(null, loadout, antagRoleLoadoutProto, Loc.GetString(antag.Name));
                };
            }

            antagContainer.AddChild(antagLoadoutButton);
            // Genesis-End

            AntagList.AddChild(antagContainer);
        }
    }

    /// <summary>
    /// Randomizes job priorities while keeping the single-high-priority rule enforced by the profile.
    /// </summary>
    private void RandomizeJobPreferences(IRobustRandom random)
    {
        if (Profile is null)
            return;

        var jobs = _prototypeManager.EnumeratePrototypes<JobPrototype>()
            .Where(job => job.SetPreference && _requirements.IsAllowed(job, Profile, out _))
            .ToArray();

        if (jobs.Length == 0)
            return;

        var highPriorityJob = random.Pick(jobs);
        var otherPriorities = new[] { JobPriority.Never, JobPriority.Low, JobPriority.Medium };
        var priorities = new Dictionary<ProtoId<JobPrototype>, JobPriority>();

        foreach (var job in jobs)
        {
            if (job.ID == highPriorityJob.ID)
            {
                priorities[job.ID] = JobPriority.High;
                continue;
            }

            var priority = random.Pick(otherPriorities);
            if (priority != JobPriority.Never)
                priorities[job.ID] = priority;
        }

        Profile = Profile.WithJobPriorities(priorities);
    }

    /// <summary>
    /// Randomizes antagonist opt-ins, excluding roles the current account cannot select.
    /// </summary>
    private void RandomizeAntagPreferences(IRobustRandom random)
    {
        if (Profile is null)
            return;

        foreach (var antag in Profile.AntagPreferences.ToArray())
            Profile = Profile.WithAntagPreference(antag, false);

        var antags = _prototypeManager.EnumeratePrototypes<AntagPrototype>()
            .Where(antag => antag.SetPreference && _requirements.IsAllowed(antag, Profile, out _))
            .ToArray();

        foreach (var antag in antags)
        {
            if (random.NextFloat(0f, 1f) < 0.35f)
                Profile = Profile.WithAntagPreference(antag.ID, true);
        }
    }

    /// <summary>
    /// Randomizes traits within their existing categories. WithTraitPreference enforces each category's point limit.
    /// </summary>
    private void RandomizeTraitPreferences(IRobustRandom random)
    {
        if (Profile is null)
            return;

        foreach (var trait in Profile.TraitPreferences.ToArray())
            Profile = Profile.WithoutTraitPreference(trait, _prototypeManager);

        var categories = _prototypeManager.EnumeratePrototypes<TraitPrototype>()
            .GroupBy(trait => trait.Category ?? TraitCategoryPrototype.Default);

        foreach (var category in categories)
        {
            foreach (var trait in category.OrderBy(_ => random.NextFloat(0f, 1f)))
            {
                if (random.NextFloat(0f, 1f) < 0.4f)
                    Profile = Profile.WithTraitPreference(trait.ID, _prototypeManager);
            }
        }
    }

    /// <summary>
    /// Randomizes role loadout groups for preferred jobs and enabled antagonists.
    /// Apparel groups are only changed when "Consider clothing" is enabled.
    /// </summary>
    private void RandomizeRoleLoadouts(bool considerClothing, bool feminineClothing, IRobustRandom random)
    {
        if (Profile is null || _playerManager.LocalSession is not { } session ||
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

            // Preserve current selections as a starting point. SetDefault only fills missing groups.
            loadout.SetDefault(Profile, session, _prototypeManager);

            foreach (var groupId in roleProto.Groups)
            {
                if (!_prototypeManager.TryIndex(groupId, out var groupProto) || groupProto.Hidden)
                    continue;

                var isClothing = IsClothingLoadoutGroup(groupId.Id);
                if (isClothing && !considerClothing)
                    continue;

                var candidates = groupProto.Loadouts.ToList();
                if (candidates.Count == 0)
                    continue;

                // Jumpsuit and jumpskirt prototypes are selected according to the explicit clothing style.
                if (groupId.Id.Contains("Jumpsuit", StringComparison.OrdinalIgnoreCase))
                {
                    var styleCandidates = feminineClothing
                        ? candidates.Where(candidate =>
                            candidate.Id.Contains("skirt", StringComparison.OrdinalIgnoreCase)).ToList()
                        : candidates.Where(candidate =>
                            !candidate.Id.Contains("skirt", StringComparison.OrdinalIgnoreCase) &&
                            (candidate.Id.Contains("jumpsuit", StringComparison.OrdinalIgnoreCase) ||
                             candidate.Id.Contains("suit", StringComparison.OrdinalIgnoreCase))).ToList();

                    if (styleCandidates.Count > 0)
                        candidates = styleCandidates;
                }

                if (!loadout.SelectedLoadouts.TryGetValue(groupId, out var selectedLoadouts))
                {
                    selectedLoadouts = new List<Loadout>();
                    loadout.SelectedLoadouts[groupId] = selectedLoadouts;
                }

                selectedLoadouts.Clear();
                var selected = random.Pick(candidates);
                loadout.AddLoadout(groupId, selected, _prototypeManager);
            }

            loadout.EnsureValid(Profile, session, collection);
            Profile = Profile.WithLoadout(loadout);
        }
    }

    private static bool IsClothingLoadoutGroup(string groupId)
    {
        string[] clothingSlots =
        [
            "Jumpsuit", "Uniform", "OuterClothing", "Head", "Neck", "Shoes",
            "Gloves", "Mask", "Glasses", "Backpack", "Satchel", "Duffel",
            "Ears", "Belt", "Wrist"
        ];

        return clothingSlots.Any(slot => groupId.Contains(slot, StringComparison.OrdinalIgnoreCase));
    }

}
