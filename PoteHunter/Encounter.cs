namespace PoteHunter;

public sealed class Encounter
{
    const long AttackEvidenceMilliseconds = 1000;
    // Recorded collateral hits reach beyond three units during a basic combo/skill sweep.
    // This bounds damage attribution, independently of the optional nearby-target clearing radius.
    public static double CollateralReach(double meleeRange) => Math.Max(5, meleeRange + 1.25);
    readonly Dictionary<Identity, Member> members = new();
    List<Entity> candidates = new();
    List<Entity> engagedCandidates = new();
    List<Entity> trackedEngagedCandidates = new();
    Vec lastPlayer;
    double lastRadius;
    bool clearNearby = true;

    public bool Active { get; private set; }
    public IReadOnlyList<Entity> Candidates => candidates;
    public IReadOnlyList<Entity> EngagedCandidates => engagedCandidates;
    // Last-known identities for engaged targets whose current snapshot is temporarily missing.
    // Combat revalidates these identities before sending input.
    public IReadOnlyList<Entity> TrackedEngagedCandidates => trackedEngagedCandidates;
    public int EngagedCount => members.Values.Count(member => member.Engaged);
    public bool HasUnresolvedNearby { get; private set; }
    public bool HasEngaged { get; private set; }
    public bool HasUnresolvedEngaged { get; private set; }

    public void Begin()
    {
        Reset();
        Active = true;
    }

    public void Reset()
    {
        members.Clear();
        candidates.Clear();
        engagedCandidates.Clear();
        trackedEngagedCandidates.Clear();
        HasUnresolvedNearby = false;
        HasEngaged = false;
        HasUnresolvedEngaged = false;
        lastPlayer = default;
        lastRadius = 0;
        clearNearby = true;
        Active = false;
    }

    public void Observe(IEnumerable<Entity> entities, IReadOnlyDictionary<uint, Health> health, Vec player, double radius,
        Func<Entity, Health, bool> mayJoin, Func<Entity, Health, bool>? mayRemainEngaged = null, bool clearNearby = true,
        Func<Entity, bool>? mayClaimCollateral = null, bool attackHeld = false, double attackReach = 0,Action<Entity>? confirmedKill=null)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(mayJoin);
        if (!player.Finite) throw new ArgumentOutOfRangeException(nameof(player));
        if (!double.IsFinite(radius) || radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
        if (!double.IsFinite(attackReach) || attackReach < 0) throw new ArgumentOutOfRangeException(nameof(attackReach));
        if (!Active) { candidates.Clear(); engagedCandidates.Clear(); HasUnresolvedNearby = HasEngaged = HasUnresolvedEngaged = false; return; }
        lastPlayer = player;
        lastRadius = radius;
        this.clearNearby = clearNearby;
        bool trackCollateralEngagement = mayRemainEngaged != null || mayClaimCollateral != null;
        var mayRemain = mayRemainEngaged ?? mayJoin;

        // Keep the complete identity map: a creature leaving the admission radius has not died.
        var observed = entities.Where(e => e.Monster && !e.PriorityLootObject && e.Position.Finite).ToArray();
        var ambiguousIds = observed.GroupBy(e => e.Id).Where(group => group.Select(Identity.Of).Distinct().Skip(1).Any())
            .Select(group => group.Key).ToHashSet();
        var visible = observed.Where(e => !ambiguousIds.Contains(e.Id))
            .GroupBy(Identity.Of).ToDictionary(group => group.Key, group => group.First());

        foreach (var (identity, member) in members.ToArray())
        {
            // Generation/address reuse must never inherit damage ownership or a combat lock.
            if (ambiguousIds.Contains(identity.Id))
            {
                member.Visible = false;
                member.SafeToAttack = false;
                if (member.Engaged) member.UnknownHealthObservations++;
                else members.Remove(identity);
                continue;
            }
            if (visible.Keys.Any(other => other.Id == identity.Id && other != identity))
            { members.Remove(identity); continue; }
            if (health.GetValueOrDefault(identity.Id).Dead)
            {
                if(member.Engaged && (member.AttackSent || member.MayHaveReceivedOurDamage))
                    confirmedKill?.Invoke(visible.GetValueOrDefault(identity) ?? member.Entity);
                members.Remove(identity);continue;
            }
            member.Visible = false;
            member.SafeToAttack = false;
            if (!clearNearby) member.LegacyEligible = false;
            if (!visible.ContainsKey(identity))
            {
                if (!member.Engaged) members.Remove(identity);
                else member.UnknownHealthObservations++;
            }
        }

        long now = Environment.TickCount64;
        foreach (var (identity, entity) in visible)
        {
            Health hp = health.GetValueOrDefault(entity.Id);
            if (hp.Dead) { members.Remove(identity); continue; }
            double distance = (entity.Position - player).Length;
            bool nearby = distance <= radius;
            bool inMonitoringRange = distance <= Math.Max(radius, attackReach);

            if (members.TryGetValue(identity, out var member))
            {
                // Combo and charged-skill delays keep the attack held between main-loop iterations.
                // Refresh evidence before consuming this HP change, using the current entity position.
                if (attackHeld && distance <= attackReach && member.Health.Known &&
                    member.Health.Current == member.Health.Maximum)
                    member.ReachableAttackAt = now;
                bool recentAttack = member.ReachableAttackAt != long.MinValue && now >= member.ReachableAttackAt &&
                    now - member.ReachableAttackAt <= AttackEvidenceMilliseconds;
                bool recentDamage = hp.Known && member.ObservedFullHealth && hp.Current < hp.Maximum && hp.Current < member.Health.Current && recentAttack;
                if (!inMonitoringRange && !member.Engaged && !(trackCollateralEngagement && recentAttack)) { members.Remove(identity); continue; }
                member.Entity = entity;
                member.Visible = true;
                member.Nearby = nearby;
                if (!hp.Known)
                {
                    member.UnknownHealthObservations++;
                    continue;
                }

                member.UnknownHealthObservations = 0;
                bool wasEngaged = member.Engaged;
                bool hadDamageEvidence = member.MayHaveReceivedOurDamage;
                bool claimAccepted = mayClaimCollateral?.Invoke(entity) ?? true;
                if (recentDamage && claimAccepted)
                {
                    // The safety callback can inspect this tentative evidence for its anti-KS decision.
                    member.MayHaveReceivedOurDamage = true;
                    if (trackCollateralEngagement) member.Engaged = true;
                }
                bool safe;
                try { safe = mayRemain(entity, hp); }
                catch
                {
                    member.Engaged = wasEngaged;
                    member.MayHaveReceivedOurDamage = hadDamageEvidence;
                    throw;
                }
                if (!safe && mayClaimCollateral == null)
                {
                    member.Engaged = wasEngaged;
                    member.MayHaveReceivedOurDamage = hadDamageEvidence;
                }
                member.SafeToAttack = member.Engaged && safe;
                member.LegacyEligible = clearNearby && nearby && mayJoin(entity, hp);
                bool monitor = (inMonitoringRange || trackCollateralEngagement && recentAttack) && hp.Current == hp.Maximum &&
                    (mayClaimCollateral != null ? claimAccepted : safe);
                if (!member.Engaged && !member.LegacyEligible && !monitor)
                { members.Remove(identity); continue; }
                if (hp.Current == hp.Maximum) member.ObservedFullHealth = true;
                member.Health = hp;
            }
            else
            {
                if (!inMonitoringRange || !hp.Known) continue;
                bool eligible = clearNearby && nearby && mayJoin(entity, hp);
                bool monitor = hp.Current == hp.Maximum && (mayClaimCollateral?.Invoke(entity) ?? mayRemain(entity, hp));
                if (!eligible && !monitor) continue;
                members.Add(identity, new Member(entity, hp)
                {
                    ObservedFullHealth = hp.Current == hp.Maximum, Visible = true, Nearby = nearby, LegacyEligible = eligible,
                    ReachableAttackAt = attackHeld && distance <= attackReach && hp.Current == hp.Maximum ? now : long.MinValue
                });
            }
        }

        UpdateCandidates();
    }

    public bool IsEngaged(Entity entity) => members.TryGetValue(Identity.Of(entity), out var member) && member.Engaged;

    // Call only after our attack input succeeds against the freshly validated target.
    public void MarkAttack(Entity entity, Health health)
    {
        if (!entity.Monster || entity.PriorityLootObject || !entity.Position.Finite || !health.Known || health.Dead) return;
        EnrollEngaged(entity,health);
        if(members.TryGetValue(Identity.Of(entity),out var member))member.AttackSent=true;
    }

    // The caller has measured incoming player damage and selected a permitted
    // defensive candidate. Enrollment keeps that fight ahead of fresh hunting;
    // it does not assert that we attacked or caused any of this creature's damage.
    public void MarkDefensive(Entity entity, Health health) => EnrollEngaged(entity,health);

    void EnrollEngaged(Entity entity, Health health)
    {
        if (!entity.Monster || entity.PriorityLootObject || !entity.Position.Finite || !health.Known || health.Dead) return;
        if (!Active) Begin();
        var identity = Identity.Of(entity);
        foreach (var old in members.Keys.Where(old => old.Id == identity.Id && old != identity).ToArray()) members.Remove(old);
        if (!members.TryGetValue(identity, out var member)) members.Add(identity, member = new Member(entity, health));
        member.Entity = entity;
        member.Health = health;
        member.Engaged = true;
        member.Visible = true;
        member.SafeToAttack = true;
        member.Nearby = (entity.Position - lastPlayer).Length <= lastRadius;
        member.UnknownHealthObservations = 0;
        if (health.Current == health.Maximum) member.ObservedFullHealth = true;
        UpdateCandidates();
    }

    void UpdateCandidates()
    {
        static bool Readable(Member member) => member.Visible && member.Health.Known && !member.Health.Dead && member.UnknownHealthObservations == 0;
        engagedCandidates = members.Values.Where(member => member.Engaged && member.SafeToAttack && Readable(member))
            .OrderBy(member => (member.Entity.Position - lastPlayer).Length).ThenBy(member => member.Entity.Id)
            .Select(member => member.Entity).ToList();
        // A missing world snapshot increments UnknownHealthObservations while
        // preserving the last known identity. Keep that identity in the engaged
        // queue so combat can reacquire it instead of selecting fresh work or
        // stopping with a false no-target state. Protected/blocked engagements
        // remain excluded when their HP is still readable.
        trackedEngagedCandidates = members.Values.Where(member => member.Engaged && !member.Health.Dead &&
                (member.SafeToAttack || member.UnknownHealthObservations > 0))
            .OrderBy(member => (member.Entity.Position - lastPlayer).Length).ThenBy(member => member.Entity.Id)
            .Select(member => member.Entity).ToList();
        candidates = members.Values.Where(member => Readable(member) && (member.Engaged ? member.SafeToAttack : clearNearby && member.Nearby && member.LegacyEligible))
            .OrderBy(member => (member.Entity.Position - lastPlayer).Length).ThenBy(member => member.Entity.Id)
            .Select(member => member.Entity).ToList();
        HasEngaged = members.Values.Any(member => member.Engaged);
        HasUnresolvedEngaged = members.Values.Any(member => member.Engaged && (!member.SafeToAttack || !Readable(member)));
        HasUnresolvedNearby = HasUnresolvedEngaged || members.Values.Any(member => !member.Engaged && member.Nearby && member.LegacyEligible && member.UnknownHealthObservations > 0);
    }

    public bool MayHaveReceivedOurDamage(Entity entity) =>
        members.TryGetValue(Identity.Of(entity), out var member) && member.MayHaveReceivedOurDamage;

    public void Forget(Entity entity)
    {
        members.Remove(Identity.Of(entity));
        UpdateCandidates();
    }

    public void NoteAttack(Vec player, double reachRadius)
    {
        if (!Active) return;
        if (!player.Finite) throw new ArgumentOutOfRangeException(nameof(player));
        if (!double.IsFinite(reachRadius) || reachRadius < 0) throw new ArgumentOutOfRangeException(nameof(reachRadius));
        long now = Environment.TickCount64;
        foreach (var member in members.Values.Where(member => member.Visible && member.UnknownHealthObservations == 0 && member.Health.Known && !member.Health.Dead &&
            member.Health.Current == member.Health.Maximum && (member.Entity.Position - player).Length <= reachRadius))
            member.ReachableAttackAt = now;
    }

    readonly record struct Identity(uint Id, uint Generation, long Address)
    {
        public static Identity Of(Entity entity) => new(entity.Id, entity.Generation, entity.Address);
    }

    sealed class Member(Entity entity, Health health)
    {
        public Entity Entity = entity;
        public Health Health = health;
        public bool ObservedFullHealth;
        public bool MayHaveReceivedOurDamage;
        public bool AttackSent;
        public bool Engaged;
        public bool Visible;
        public bool Nearby;
        public bool SafeToAttack;
        public bool LegacyEligible;
        public long ReachableAttackAt = long.MinValue;
        public int UnknownHealthObservations;
    }

    public static void SelfTest()
    {
        static Entity Mob(uint id, double distance, uint generation = 1, long address = 0) =>
            new(address == 0 ? id * 100 : address, 0x80000000u | id, $"Lv. 1 Mob {id}", new Vec(distance, 0), 0, Generation: generation);
        static Dictionary<uint, Health> Hp(params (Entity Entity, Health Health)[] values) =>
            values.ToDictionary(value => value.Entity.Id, value => value.Health);

        Vec player = new(0, 0);
        var first = Mob(1, 3); var second = Mob(2, 1); var third = Mob(3, 2);
        var encounter = new Encounter(); encounter.Begin();
        encounter.Observe([first, second, third], Hp((first, new(100, 100)), (second, new(100, 100)), (third, new(100, 100))), player, 20, (_, _) => true);
        if (encounter.Candidates.Count != 3 || encounter.Candidates[0].Id != second.Id) throw new Exception("Encounter did not retain and order three mobs.");
        encounter.Observe([first, second, third], Hp((first, new(0, 100)), (second, new(100, 100)), (third, new(100, 100))), player, 20, (_, _) => true);
        if (encounter.Candidates.Count != 2 || encounter.Candidates.Any(e => e.Id == first.Id)) throw new Exception("Dead encounter member was not removed.");

        var wounded = Mob(4, 4);
        encounter.Observe([second, third, wounded], Hp((second, new(100, 100)), (third, new(100, 100)), (wounded, new(50, 100))), player, 20,
            (entity, hp) => hp.Current == hp.Maximum);
        if (encounter.Candidates.Any(e => e.Id == wounded.Id)) throw new Exception("Unowned wounded entrant joined the encounter.");

        var clean = new Encounter(); var near = Mob(5, 2); var far = Mob(6, 12);
        clean.Begin(); clean.Observe([near, far], Hp((near, new(100, 100)), (far, new(100, 100))), player, 20, (_, _) => true);
        clean.Observe([near, far], Hp((near, new(90, 100)), (far, new(100, 100))), player, 20,
            (entity, hp) => hp.Current == hp.Maximum || clean.MayHaveReceivedOurDamage(entity));
        if (clean.MayHaveReceivedOurDamage(near) || clean.Candidates.Any(e => e.Id == near.Id)) throw new Exception("Damage was attributed before our attack input.");

        var collateral = new Encounter(); collateral.Begin();
        collateral.Observe([near, far], Hp((near, new(100, 100)), (far, new(100, 100))), player, 20, (_, _) => true);
        collateral.NoteAttack(player, 3);
        collateral.Observe([near, far], Hp((near, new(90, 100)), (far, new(90, 100))), player, 20,
            (entity, hp) => hp.Current == hp.Maximum || collateral.MayHaveReceivedOurDamage(entity));
        if (!collateral.MayHaveReceivedOurDamage(near) || !collateral.Candidates.Any(e => e.Id == near.Id) ||
            collateral.MayHaveReceivedOurDamage(far) || collateral.Candidates.Any(e => e.Id == far.Id))
            throw new Exception("Recent reachable attack evidence was not bounded to nearby collateral damage.");

        collateral.Observe([near with { Position = new Vec(30, 0) }], Hp((near, new(80, 100))), player, 20, (_, _) => true);
        if (collateral.Candidates.Count != 0) throw new Exception("Outside-radius member was retained.");

        var reuse = new Encounter(); var original = Mob(7, 2, generation: 1, address: 700);
        reuse.Begin(); reuse.Observe([original], Hp((original, new(100, 100))), player, 20, (_, _) => true); reuse.NoteAttack(player, 3);
        var replacement = Mob(7, 2, generation: 2, address: 701);
        reuse.Observe([replacement], Hp((replacement, new(90, 100))), player, 20,
            (entity, hp) => hp.Current == hp.Maximum || reuse.MayHaveReceivedOurDamage(entity));
        if (reuse.MayHaveReceivedOurDamage(replacement) || reuse.Candidates.Count != 0) throw new Exception("UID reuse inherited prior attack evidence.");

        var uncertain = new Encounter(); uncertain.Begin();
        uncertain.Observe([second], Hp((second, new(100, 100))), player, 20, (_, _) => true);
        uncertain.Observe([second], new Dictionary<uint, Health>(), player, 20, (_, _) => throw new Exception("Unknown HP must not invoke mayJoin."));
        if (!uncertain.HasUnresolvedNearby || uncertain.Candidates.Count != 0) throw new Exception("Briefly unknown HP was not preserved as unresolved.");
        for(int i=0;i<12;i++) uncertain.Observe([second],new Dictionary<uint,Health>(),player,20,(_,_)=>true);
        if(!uncertain.HasUnresolvedNearby) throw new Exception("Unknown HP was incorrectly treated as a cleared enemy");
        uncertain.Observe([], new Dictionary<uint, Health>(), player, 20, (_, _) => true);
        if (uncertain.HasUnresolvedNearby) throw new Exception("Despawned unresolved member was retained.");

        collateral.Begin(); collateral.Observe([near],Hp((near,new(100,100))),player,6,(_,_)=>true); collateral.NoteAttack(player,3);
        collateral.Observe([near],Hp((near,new(90,100))),player,6,(_,_)=>true); collateral.Forget(near);
        collateral.Observe([near],Hp((near,new(90,100))),player,6,(entity,hp)=>hp.Current==hp.Maximum || collateral.MayHaveReceivedOurDamage(entity));
        if(collateral.Candidates.Count!=0 || collateral.MayHaveReceivedOurDamage(near)) throw new Exception("A relinquished enemy retained encounter damage permission");

        var engagement = new Encounter(); var chosen = Mob(10, 2); var incidental = Mob(11, 2.5); var untouched = Mob(12, 2.7);
        engagement.Begin();
        engagement.Observe([chosen, incidental, untouched], Hp((chosen, new(100, 100)), (incidental, new(100, 100)), (untouched, new(100, 100))),
            player, 6, (entity, _) => entity.Id == chosen.Id, (_, _) => true, clearNearby: false);
        if (engagement.HasEngaged || engagement.Candidates.Count != 0 || engagement.HasUnresolvedNearby)
            throw new Exception("Untouched proximity monitors were treated as engaged or selected with nearby clearing disabled.");
        engagement.MarkAttack(chosen, new(100, 100));
        engagement.NoteAttack(player, 3);
        engagement.Observe([chosen, incidental, untouched], Hp((chosen, new(90, 100)), (incidental, new(80, 100)), (untouched, new(100, 100))),
            player, 6, (entity, _) => entity.Id == chosen.Id, (entity, hp) => hp.Current == hp.Maximum || engagement.IsEngaged(entity), clearNearby: false);
        if (engagement.EngagedCount != 2 || !engagement.IsEngaged(chosen) || !engagement.IsEngaged(incidental) || engagement.IsEngaged(untouched) ||
            !engagement.MayHaveReceivedOurDamage(incidental) || engagement.EngagedCandidates.Count != 2)
            throw new Exception("Direct combat and nearby off-filter collateral did not form a two-enemy encounter.");
        var moved = incidental with { Position = new Vec(10, 0) };
        engagement.Observe([chosen, moved], Hp((chosen, new(90, 100)), (moved, new(70, 100))), player, 6, (_, _) => false, (_, _) => true, clearNearby: false);
        if (engagement.EngagedCandidates.Count != 2 || engagement.HasUnresolvedEngaged)
            throw new Exception("An engaged enemy leaving the nearby admission radius was forgotten.");
        engagement.Observe([chosen, moved], Hp((chosen, new(90, 100))), player, 6, (_, _) => false, (_, _) => true, clearNearby: false);
        if (!engagement.HasEngaged || !engagement.HasUnresolvedEngaged || !engagement.HasUnresolvedNearby || engagement.EngagedCandidates.Count != 1)
            throw new Exception("Unknown engaged HP did not block clearing while preserving readable opponents.");
        engagement.Observe([], new Dictionary<uint, Health>(), player, 6, (_, _) => false, (_, _) => true, clearNearby: false);
        if (engagement.EngagedCount != 2 || !engagement.HasUnresolvedEngaged || engagement.EngagedCandidates.Count != 0)
            throw new Exception("A missing snapshot silently cleared an engaged enemy or exposed a stale attack target.");
        engagement.Observe([], Hp((chosen, new(0, 100)), (incidental, new(0, 100))), player, 6, (_, _) => false, (_, _) => true, clearNearby: false);
        if (engagement.HasEngaged) throw new Exception("Confirmed dead HP did not resolve absent engaged enemies.");

        var protectedEngagement = new Encounter(); protectedEngagement.MarkAttack(chosen, new(100, 100));
        protectedEngagement.Observe([chosen], Hp((chosen, new(90, 100))), player, 6, (_, _) => true, (_, _) => false);
        if (!protectedEngagement.HasUnresolvedEngaged || protectedEngagement.EngagedCandidates.Count != 0 || protectedEngagement.Candidates.Count != 0)
            throw new Exception("A protected engaged target was attacked or silently forgotten.");
        var newLife = chosen with { Generation = chosen.Generation + 1, Address = chosen.Address + 1 };
        protectedEngagement.Observe([newLife], Hp((newLife, new(100, 100))), player, 6, (_, _) => false, (_, _) => true, clearNearby: false);
        if (protectedEngagement.HasEngaged || protectedEngagement.IsEngaged(newLife))
            throw new Exception("A replacement creature inherited an old engagement.");

        var rejectedCollateral = new Encounter(); rejectedCollateral.Begin();
        rejectedCollateral.Observe([incidental], Hp((incidental, new(100, 100))), player, 6, (_, _) => false, (_, _) => true, clearNearby: false);
        rejectedCollateral.NoteAttack(player, 3);
        rejectedCollateral.Observe([incidental], Hp((incidental, new(90, 100))), player, 6, (_, _) => false, (_, _) => false, clearNearby: false);
        if (rejectedCollateral.HasEngaged || rejectedCollateral.MayHaveReceivedOurDamage(incidental))
            throw new Exception("Collateral evidence overrode a protection rejection.");

        var boundaryCollateral = new Encounter(); boundaryCollateral.Begin();
        boundaryCollateral.Observe([incidental], Hp((incidental, new(100, 100))), player, 6, (_, _) => false, (_, _) => false,
            clearNearby: false, mayClaimCollateral: _ => true);
        if (boundaryCollateral.Candidates.Count != 0 || boundaryCollateral.HasEngaged)
            throw new Exception("A blocked collateral monitor was selected before receiving damage.");
        boundaryCollateral.NoteAttack(player, 3);
        boundaryCollateral.Observe([moved], Hp((moved, new(90, 100))), player, 6, (_, _) => false, (_, _) => false,
            clearNearby: false, mayClaimCollateral: _ => true);
        if (!boundaryCollateral.IsEngaged(moved) || !boundaryCollateral.HasUnresolvedEngaged || boundaryCollateral.EngagedCandidates.Count != 0)
            throw new Exception("Credible collateral was forgotten when it crossed the admission radius or became blocked.");
        boundaryCollateral.Observe([moved], Hp((moved, new(80, 100))), player, 6, (_, _) => false, (_, _) => true,
            clearNearby: false, mayClaimCollateral: _ => true);
        if (boundaryCollateral.EngagedCandidates.Count != 1 || boundaryCollateral.HasUnresolvedEngaged)
            throw new Exception("A retained collateral engagement could not resume once its protection cleared.");

        var disputed = new Encounter(); disputed.Begin();
        disputed.Observe([incidental], Hp((incidental, new(100, 100))), player, 6, (_, _) => false, (_, _) => true,
            clearNearby: false, mayClaimCollateral: _ => true);
        disputed.NoteAttack(player, 3);
        disputed.Observe([incidental], Hp((incidental, new(90, 100))), player, 6, (_, _) => false, (_, _) => true,
            clearNearby: false, mayClaimCollateral: _ => false);
        if (disputed.HasEngaged || disputed.MayHaveReceivedOurDamage(incidental))
            throw new Exception("A rejected damage-ownership claim enrolled an engaged target.");

        var expiredEvidence = new Encounter(); expiredEvidence.Begin();
        expiredEvidence.Observe([incidental], Hp((incidental, new(100, 100))), player, 6, (_, _) => false, (_, _) => true,
            clearNearby: false, mayClaimCollateral: _ => true);
        expiredEvidence.NoteAttack(player, 3);
        expiredEvidence.members[Identity.Of(incidental)].ReachableAttackAt = Environment.TickCount64 - AttackEvidenceMilliseconds - 1;
        expiredEvidence.Observe([incidental], Hp((incidental, new(90, 100))), player, 6, (_, _) => false, (_, _) => true,
            clearNearby: false, mayClaimCollateral: _ => true);
        if (expiredEvidence.HasEngaged) throw new Exception("An expired attack window claimed unrelated later damage.");

        var ambiguous = new Encounter(); ambiguous.MarkAttack(chosen, new(100, 100));
        ambiguous.Observe([chosen, newLife], Hp((chosen, new(90, 100))), player, 6, (_, _) => true, (_, _) => true);
        if (!ambiguous.IsEngaged(chosen) || !ambiguous.HasUnresolvedEngaged || ambiguous.EngagedCandidates.Count != 0 || ambiguous.IsEngaged(newLife))
            throw new Exception("An ambiguous identity snapshot erased or transferred an engagement.");
        ambiguous.Observe([newLife], Hp((newLife, new(100, 100))), player, 6, (_, _) => true, (_, _) => true);
        if (ambiguous.HasEngaged) throw new Exception("A later unambiguous replacement did not release the old identity.");

        // Replay the missed side-hit shape: full HP outside three units, damage during a held
        // combo/charge, then the primary dies while the off-filter monster remains alive.
        var sweep = new Encounter(); sweep.Begin();
        var wolf = Mob(40, 3.9); var outsideSweep = Mob(41, 6);
        var preWounded = Mob(42, 2); var sideUntouched = Mob(43, 2.4);
        double sweepReach = CollateralReach(1);
        sweep.Observe([chosen, wolf, outsideSweep, preWounded, sideUntouched],
            Hp((chosen,new(100,100)),(wolf,new(1021,1021)),(outsideSweep,new(100,100)),(preWounded,new(80,100)),(sideUntouched,new(100,100))),
            player, 1, (_,_)=>false, (_,_)=>true, clearNearby:false, mayClaimCollateral:_=>true, attackReach:sweepReach);
        sweep.MarkAttack(chosen,new(100,100));
        // The main loop has not refreshed its one-shot evidence during a charged skill.
        sweep.members[Identity.Of(wolf)].ReachableAttackAt = Environment.TickCount64 - AttackEvidenceMilliseconds - 1;
        wolf=wolf with {Position=new Vec(3.17,0)};
        sweep.Observe([chosen, wolf, outsideSweep, preWounded, sideUntouched],
            Hp((chosen,new(10,100)),(wolf,new(951,1021)),(outsideSweep,new(90,100)),(preWounded,new(80,100)),(sideUntouched,new(100,100))),
            player, 1, (_,_)=>false, (_,_)=>true, clearNearby:false, mayClaimCollateral:_=>true, attackHeld:true, attackReach:sweepReach);
        if (!sweep.IsEngaged(wolf) || sweep.EngagedCount!=2 || sweep.IsEngaged(outsideSweep) || sweep.IsEngaged(preWounded) || sweep.IsEngaged(sideUntouched))
            throw new Exception("Held sweep damage outside three units was missed or unrelated neighbors were claimed.");
        int sweepFreshSelections=0;
        Entity? SweepFresh() { sweepFreshSelections++;return untouched; }
        sweep.Observe([chosen,wolf],Hp((chosen,new(0,100)),(wolf,new(653,1021))),player,1,(_,_)=>false,(_,_)=>true,
            clearNearby:false,mayClaimCollateral:_=>true,attackReach:sweepReach);
        if (Targeting.ChooseEngagedFirst(sweep,SweepFresh)?.Id!=wolf.Id || sweepFreshSelections!=0)
            throw new Exception("Primary death allowed fresh selection before finishing a side-hit monster.");
        sweep.Observe([wolf],Hp((wolf,new(1021,1021))),player,1,(_,_)=>false,(_,_)=>true,clearNearby:false,attackReach:sweepReach);
        if (Targeting.ChooseEngagedFirst(sweep,SweepFresh)?.Id!=wolf.Id || sweepFreshSelections!=0)
            throw new Exception("An engaged side-hit monster regenerating to full was treated as a finished fight.");
        sweep.Observe([wolf],Hp((wolf,new(0,1021))),player,1,(_,_)=>false,(_,_)=>true,clearNearby:false,attackReach:sweepReach);
        if (Targeting.ChooseEngagedFirst(sweep,SweepFresh)?.Id!=untouched.Id || sweepFreshSelections!=1)
            throw new Exception("Fresh selection did not resume after the side-hit monster died.");

        var entrant = new Encounter(); entrant.Begin();
        var entering = Mob(44, 7);
        entrant.Observe([entering],Hp((entering,new(100,100))),player,8,(_,_)=>false,(_,_)=>true,clearNearby:false,attackReach:sweepReach);
        entering=entering with {Position=new Vec(3.5,0)};
        entrant.Observe([entering],Hp((entering,new(90,100))),player,8,(_,_)=>false,(_,_)=>true,clearNearby:false,
            mayClaimCollateral:_=>true,attackHeld:true,attackReach:sweepReach);
        if (!entrant.IsEngaged(entering))throw new Exception("An observed full-health monster entering a held attack was missed.");

        var departing = new Encounter(); departing.Begin();
        departing.Observe([wolf],Hp((wolf,new(1021,1021))),player,8,(_,_)=>false,(_,_)=>true,clearNearby:false,attackReach:sweepReach);
        var awayWolf=wolf with {Position=new Vec(7,0)};
        departing.Observe([awayWolf],Hp((awayWolf,new(951,1021))),player,8,(_,_)=>false,(_,_)=>true,clearNearby:false,
            mayClaimCollateral:_=>true,attackHeld:true,attackReach:sweepReach);
        if (departing.HasEngaged)throw new Exception("Stale position attributed outside-reach damage to a held attack.");

        var newcomer = new Encounter(); newcomer.Begin();
        newcomer.Observe([wolf],Hp((wolf,new(1021,1021))),player,0,(_,_)=>false,(_,_)=>true,clearNearby:false,
            mayClaimCollateral:_=>true,attackHeld:true,attackReach:sweepReach);
        newcomer.Observe([wolf],Hp((wolf,new(951,1021))),player,0,(_,_)=>false,(_,_)=>true,clearNearby:false,
            mayClaimCollateral:_=>true,attackReach:sweepReach);
        if (!newcomer.IsEngaged(wolf))throw new Exception("A new full-health monitor lost the final held hit after release.");

        var delayedSweep=new Encounter();delayedSweep.Begin();
        delayedSweep.Observe([wolf],Hp((wolf,new(1021,1021))),player,1,(_,_)=>false,(_,_)=>true,clearNearby:false,
            mayClaimCollateral:_=>true,attackHeld:true,attackReach:sweepReach);
        delayedSweep.Observe([awayWolf],Hp((awayWolf,new(1021,1021))),player,1,(_,_)=>false,(_,_)=>true,clearNearby:false,
            mayClaimCollateral:_=>true,attackReach:sweepReach);
        delayedSweep.Observe([awayWolf],Hp((awayWolf,new(951,1021))),player,1,(_,_)=>false,(_,_)=>true,clearNearby:false,
            mayClaimCollateral:_=>true,attackReach:sweepReach);
        if (!delayedSweep.IsEngaged(awayWolf))throw new Exception("A side-hit monitor moving out before delayed damage was forgotten.");

        var stoppedSweep=new Encounter();stoppedSweep.Begin();
        stoppedSweep.Observe([wolf],Hp((wolf,new(1021,1021))),player,5,(_,_)=>false,(_,_)=>true,clearNearby:false,attackReach:sweepReach);
        stoppedSweep.Observe([wolf],Hp((wolf,new(951,1021))),player,5,(_,_)=>false,(_,_)=>true,clearNearby:false,
            mayClaimCollateral:_=>true,attackReach:sweepReach);
        if (stoppedSweep.HasEngaged)throw new Exception("Proximity damage was claimed without a successful attack.");

        // A newly spawned, full-health creature can provoke a defensive response
        // without any earlier outgoing-hit snapshot. Preserve existing fights
        // while keeping incoming-damage inference separate from our hit evidence.
        var defense=new Encounter();var defender=Mob(50,1.5);var keeper=Mob(5970,4) with {Model="MON_SnowGun2.GCMDS"};
        defense.MarkAttack(chosen,new(90,100));
        defense.MarkDefensive(defender,new(100,100));
        if(!defense.Active || defense.EngagedCount!=2 || !defense.IsEngaged(chosen) || !defense.IsEngaged(defender) ||
            defense.MayHaveReceivedOurDamage(defender) || !defense.EngagedCandidates.Any(entity=>entity.Id==defender.Id))
            throw new Exception("Defensive enrollment lost an existing fight, missed a fresh creature, or claimed outgoing damage.");
        defense.Observe([chosen,defender],Hp((chosen,new(90,100)),(defender,new(90,100))),player,5,(_,_)=>false,(_,_)=>true,
            clearNearby:false,attackReach:5);
        if(defense.MayHaveReceivedOurDamage(defender) || defense.EngagedCount!=2)
            throw new Exception("A defensive candidate's later HP loss fabricated successful attack evidence.");
        int defensiveFreshSelections=0;
        Entity? DefensiveFresh(){defensiveFreshSelections++;return untouched;}
        if(GamekeeperPriority.ChooseFirst(defense,keeper,DefensiveFresh)?.Id!=keeper.Id || defensiveFreshSelections!=0 ||
            defense.EngagedCount!=2 || !defense.IsEngaged(defender))
            throw new Exception("A defensive fight blocked Gamekeeper priority or disappeared during its interruption.");
        defense.Observe([chosen,defender],Hp((chosen,new(90,100)),(defender,new(0,100))),player,5,(_,_)=>false,(_,_)=>true,clearNearby:false);
        if(defense.IsEngaged(defender) || defense.EngagedCount!=1 || !defense.IsEngaged(chosen))
            throw new Exception("A defensive candidate's death failed to resolve only its own engagement.");
        var freshDefense=new Encounter();freshDefense.MarkDefensive(defender,new(100,100));
        if(!freshDefense.Active || freshDefense.EngagedCount!=1 || freshDefense.MayHaveReceivedOurDamage(defender))
            throw new Exception("Defensive enrollment could not open a new encounter without outgoing damage ownership.");
        freshDefense.MarkDefensive(Mob(51,1),new(0,100));freshDefense.MarkDefensive(Mob(52,1),new(0,0));
        freshDefense.MarkDefensive(Mob(53,double.NaN),new(100,100));
        freshDefense.MarkDefensive(Mob(5983,1) with {Model="NPC_AG_Container.gcmds"},new(1,1));
        freshDefense.MarkDefensive(defender with {Id=7,Address=701,Model="PC_MAN.GCMDS"},new(100,100));
        if(freshDefense.EngagedCount!=1)
            throw new Exception("Defensive enrollment accepted a dead, unknown, malformed, prop, or player candidate.");

        var queue = new Encounter(); queue.MarkAttack(first, new(100, 100)); queue.MarkAttack(second, new(100, 100));
        var priority = Mob(5970, 1) with { Model = "MON_SnowGun2.GCMDS" };
        int freshSelections = 0;
        Entity? FreshPriority() { freshSelections++; return priority; }
        queue.Observe([first, second], Hp((first, new(90, 100)), (second, new(80, 100))), player, 6, (_, _) => true, (_, _) => true);
        if (Targeting.ChooseEngagedFirst(queue, FreshPriority)?.Id != second.Id || freshSelections != 0)
            throw new Exception("A fresh priority target was considered before both engaged enemies were cleared.");
        queue.Observe([first, second], Hp((first, new(90, 100)), (second, new(0, 100))), player, 6, (_, _) => true, (_, _) => true);
        if (Targeting.ChooseEngagedFirst(queue, FreshPriority)?.Id != first.Id || freshSelections != 0)
            throw new Exception("A fresh target was considered after only one of two engaged enemies died.");
        queue.Observe([first], new Dictionary<uint, Health>(), player, 6, (_, _) => true, (_, _) => true);
        if (Targeting.ChooseEngagedFirst(queue, FreshPriority)?.Id != first.Id || freshSelections != 0)
            throw new Exception("A missing snapshot did not keep the last-known engaged target active.");
        queue.Observe([first], Hp((first, new(0, 100))), player, 6, (_, _) => true, (_, _) => true);
        if (Targeting.ChooseEngagedFirst(queue, FreshPriority)?.Id != priority.Id || freshSelections != 1)
            throw new Exception("Fresh target selection did not resume after both engaged enemies died.");
        queue.MarkAttack(first, new(100, 100)); queue.Forget(first);
        if (queue.HasEngaged || queue.HasUnresolvedEngaged || queue.EngagedCandidates.Count != 0)
            throw new Exception("Forgetting an engagement left a stale combat lock.");
        engagement.MarkAttack(first, new(100, 100)); engagement.Reset();
        if (engagement.Active || engagement.HasEngaged || engagement.EngagedCount != 0 || engagement.EngagedCandidates.Count != 0)
            throw new Exception("Reset retained combat engagement state.");
    }
}
