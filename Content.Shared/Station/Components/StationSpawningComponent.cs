using Content.Shared.Station.Systems;

namespace Content.Shared.Station.Components;

/// <summary>
/// Controls spawning on the given station, tracking spawners present on it.
/// </summary>
[RegisterComponent, Access(typeof(StationSpawningSystem))]
public sealed partial class StationSpawningComponent : Component
{
    // Genesis-Start
    /// <summary>
    /// Whether late joiners may use matching job spawn points on this station when no
    /// dedicated LateJoin spawn point is available.
    /// </summary>
    [DataField("use_job_spawn_points_for_late_join")]
    public bool UseJobSpawnPointsForLateJoin;
    // Genesis-End
}
