using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared.Spawners.Components;

[RegisterComponent]
public sealed partial class SpawnPointComponent : Component, ISpawnPoint
{
    /// <summary>
    /// The job this spawn point is valid for.
    /// Null will allow all jobs to spawn here.
    /// </summary>
    [DataField("job_id")]
    public ProtoId<JobPrototype>? Job;

    // Genesis-Start
    /// <summary>
    /// Departments allowed to use this spawn point. When configured, the job may match
    /// either the explicit Job field or one of these departments.
    /// </summary>
    [DataField("departments")]
    public List<ProtoId<DepartmentPrototype>> Departments = new();
    // Genesis-End

    /// <summary>
    /// The type of spawn point.
    /// </summary>
    [DataField("spawn_type"), ViewVariables(VVAccess.ReadWrite)]
    public SpawnPointType SpawnType { get; set; } = SpawnPointType.Unset;

    public override string ToString()
    {
        return $"{Job} {SpawnType}";
    }
}

public enum SpawnPointType
{
    Unset = 0,
    LateJoin,
    Job,
    Observer,
}
