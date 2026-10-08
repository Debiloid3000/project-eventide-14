using System.Linq;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Prototypes;

namespace Content.Shared.Mind.Filters;

/// <summary>
/// A mind filter that requires minds to not have a specific job.
/// This uses mind roles, not ID cards.
/// </summary>
[DataDefinition]
public sealed partial class BlacklistedJobsMindFilter
{
    [DataField]
    public List<ProtoId<JobPrototype>> Blacklist = new();

    public bool ShouldRemove(Entity<MindComponent> mind, EntityUid? exclude, IEntityManager entMan, SharedMindSystem mindSys)
    {
        var jobSys = entMan.System<SharedJobSystem>();
        return !Blacklist.Any(job => jobSys.MindHasJobWithId(mind, job));
    }
}
