namespace PoteHunter;

public static class TargetTraversalChecks
{
    public static void Run()
    {
        var self = new Entity(100, 7, "Self", new Vec(-1, 0), 10, Model: "PC_MAN.GCMDS");
        var approved = new Entity(200, 0x80000001, "Lv. 1 Green Monster", new Vec(4, 0), 10, Generation: 1);
        var excluded = new Entity(300, 0x80000002, "Lv. 1 Yellow Monster", new Vec(0, 0), 10, Generation: 1);
        Entity[] scene = [self, excluded, approved];
        var health = new Dictionary<uint, Health> { [approved.Id] = new(100, 100), [excluded.Id] = new(100, 100) };
        string[] colors = ["Green"];
        Threat Difficulty(Entity entity) => entity.Id == excluded.Id ? Threat.Yellow : Threat.Green;
        var courtesy = new CombatCourtesy();
        var avoid = Avoidance.BuildZones([], scene, self.Id);
        bool Protected(Entity entity, Health hp) => Avoidance.BlockedPoint(entity.Position, avoid) == null &&
            courtesy.Blocked(entity, hp, scene, self.Id, enabled: false, radius: 1) == null;
        bool Eligible(Entity entity, Health hp) => Targeting.Eligible(entity, hp, Difficulty(entity), "", colors) && Protected(entity, hp);

        if (avoid.Count != 0 || Eligible(excluded, health[excluded.Id]) || !Eligible(approved, health[approved.Id]))
            throw new Exception("Traversal fixture did not separate target eligibility from explicit avoidance.");
        var protectedScene = scene.Where(entity => Protected(entity, health.GetValueOrDefault(entity.Id))).ToArray();
        Entity? ChooseFresh() => Targeting.Choose(protectedScene, health, self.Position, self.Position, 20, Difficulty, "", colors);
        if (ChooseFresh()?.Id != approved.Id)
            throw new Exception("An untouched excluded monster in the approach path prevented approved target selection.");

        foreach (bool clearNearby in new[] { true, false })
        {
            var encounter = new Encounter(); encounter.Begin();
            encounter.Observe(scene, health, self.Position, 6, Eligible, Protected, clearNearby, _ => true);
            Entity? ChooseWhileActive() => Targeting.ChooseEngagedFirst(encounter, () =>
                Targeting.ChooseEncounter(protectedScene, encounter.Candidates, health, self.Position, self.Position, 20, false) ?? ChooseFresh());
            if (!encounter.Active || encounter.HasEngaged || encounter.HasUnresolvedNearby || encounter.Candidates.Any(entity => entity.Id == excluded.Id) ||
                ChooseWhileActive()?.Id != approved.Id)
                throw new Exception($"Nearby clearing={clearNearby} turned an untouched excluded monster into a traversal or combat blocker.");

            var navigation = new Navigation(); navigation.Observe("target-traversal", self.Position, self.Height); navigation.BeginGoal("approved target");
            Vec nextStep = self.Position + new Vec(2.5, 0); // The step passes directly through the excluded creature at (0, 0).
            if (navigation.Waypoint(self.Position, approved.Position, self.Position, 20, avoid) != approved.Position ||
                !navigation.CanAdvance(self.Position, nextStep, avoid) || !Targeting.BoundaryStepAllowed(self.Position, nextStep, self.Position, 20))
                throw new Exception($"Nearby clearing={clearNearby} blocked a direct approach step through an excluded monster.");

            var partialHealth = new Dictionary<uint, Health> { [approved.Id] = new(100, 100) };
            encounter.Observe(scene, partialHealth, self.Position, 6, Eligible, Protected, clearNearby, _ => true);
            if (encounter.HasUnresolvedNearby || encounter.HasEngaged || ChooseWhileActive()?.Id != approved.Id)
                throw new Exception("Unknown HP on an untouched excluded monster delayed an approved target.");

            encounter.MarkAttack(approved, health[approved.Id]);
            int freshCalls = 0;
            if (Targeting.ChooseEngagedFirst(encounter, () => { freshCalls++; return excluded; })?.Id != approved.Id || freshCalls != 0)
                throw new Exception("Allowing monster traversal weakened the existing engaged-target priority.");
        }

        var explicitAvoid = Avoidance.BuildZones([new AvoidRule("Yellow Monster", 1)], scene, self.Id);
        var avoidedNavigation = new Navigation(); avoidedNavigation.Observe("explicit-avoid", self.Position, self.Height);
        if (explicitAvoid.Count != 1 || Avoidance.BlockedSegment(self.Position, approved.Position, explicitAvoid) == null ||
            avoidedNavigation.CanAdvance(self.Position, self.Position + new Vec(2.5, 0), explicitAvoid))
            throw new Exception("An explicit monster avoidance rule was ignored by traversal.");
        try
        {
            Vec waypoint = avoidedNavigation.Waypoint(self.Position, approved.Position, self.Position, 20, explicitAvoid);
            if (waypoint == approved.Position || !avoidedNavigation.CanAdvance(self.Position, waypoint, explicitAvoid))
                throw new Exception("An explicit avoidance rule neither rejected nor safely detoured the approach.");
        }
        catch (RouteUnavailableException) { } // Starting inside this explicit avoid zone may leave no legal route.

        var wallNavigation = new Navigation(); wallNavigation.Observe("known-wall", self.Position, self.Height); wallNavigation.BeginGoal("approved target");
        wallNavigation.RecordBlock(self.Position, new Vec(1, 0), self.Height);
        if (wallNavigation.Blocked.Count != 1 || wallNavigation.CanAdvance(self.Position, self.Position + new Vec(2.5, 0), avoid))
            throw new Exception("An observed movement obstruction was discarded when walking past excluded monsters.");
        Vec detour = wallNavigation.Waypoint(self.Position, approved.Position, self.Position, 20, avoid);
        if (detour == approved.Position || !wallNavigation.CanAdvance(self.Position, detour, avoid))
            throw new Exception("An observed wall did not retain a safe approach detour.");
    }
}
