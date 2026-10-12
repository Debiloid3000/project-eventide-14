using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Shared.Spawners.Components;
using Content.Shared.Station.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared.Spawners.EntitySystems;

public sealed partial class SpawnPointSystem : EntitySystem
{
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private StationSystem _stationSystem = default!;
    [Dependency] private StationSpawningSystem _stationSpawning = default!;

    [SubscribeLocalEvent]
    private void OnPlayerSpawning(PlayerSpawningEvent args)
    {
        if (args.SpawnResult != null)
            return;

        // TODO: Cache all this if it ends up important.
        var points = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        var possiblePositions = new List<EntityCoordinates>();
        // Genesis-Start
        var jobSpecificPositions = new List<EntityCoordinates>();
        var useJobSpecificLateJoinPoint = _gameTicker.RunLevel == GameRunLevel.InRound
            && UsesJobSpecificLateJoinSpawnPoint(args.Job);
        // Genesis-End

        while (points.MoveNext(out var uid, out var spawnPoint, out var xform))
        {
            if (args.Station != null && _stationSystem.GetOwningStation(uid, xform) != args.Station)
                continue;

            if (_gameTicker.RunLevel == GameRunLevel.InRound)
            {
                // Genesis-Edit: configured jobs/departments may prefer their own spawn point on late join.
                if (useJobSpecificLateJoinPoint &&
                    spawnPoint.SpawnType == SpawnPointType.Job &&
                    args.Job != null &&
                    spawnPoint.Job == args.Job)
                {
                    jobSpecificPositions.Add(xform.Coordinates);
                }
                else if (spawnPoint.SpawnType == SpawnPointType.LateJoin)
                {
                    possiblePositions.Add(xform.Coordinates);
                }
            }
            else if (spawnPoint.SpawnType == SpawnPointType.Job &&
                     (args.Job == null || spawnPoint.Job == null || spawnPoint.Job == args.Job))
            {
                possiblePositions.Add(xform.Coordinates);
            }
        }

        // Genesis-Start
        // A special point is an opt-in preference, not a hard requirement: if absent, keep the normal late-join flow.
        if (jobSpecificPositions.Count > 0)
            possiblePositions = jobSpecificPositions;
        // Genesis-End

        if (possiblePositions.Count == 0)
        {
            // Ok we've still not returned, but we need to put them /somewhere/.
            // TODO: Refactor gameticker spawning code so we don't have to do this!
            var points2 = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();

            if (points2.MoveNext(out _, out var xform))
            {
                Log.Error($"Unable to pick a valid spawn point, picking random spawner as a backup.\nRunLevel: {_gameTicker.RunLevel} Station: {ToPrettyString(args.Station)} Job: {args.Job}");
                possiblePositions.Add(xform.Coordinates);
            }
            else
            {
                Log.Error($"No spawn points were available!\nRunLevel: {_gameTicker.RunLevel} Station: {ToPrettyString(args.Station)} Job: {args.Job}");
                return;
            }
        }

        var spawnLoc = _random.Pick(possiblePositions);

        args.SpawnResult = _stationSpawning.SpawnPlayerMob(
            spawnLoc,
            args.Job,
            args.HumanoidCharacterProfile,
            args.Station);
    }

    // Genesis-Start
    private bool UsesJobSpecificLateJoinSpawnPoint(ProtoId<JobPrototype>? job)
    {
        if (job == null || !ProtoMan.TryIndex(job.Value, out var jobPrototype))
            return false;

        if (jobPrototype.LateJoinUsesJobSpawnPoint)
            return true;

        foreach (var department in ProtoMan.EnumeratePrototypes<DepartmentPrototype>())
        {
            if (department.LateJoinUsesJobSpawnPoint && department.Roles.Contains(job.Value))
                return true;
        }

        return false;
    }
    // Genesis-End
}
