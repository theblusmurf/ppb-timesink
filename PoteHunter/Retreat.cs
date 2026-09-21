namespace PoteHunter;

public sealed class RetreatRequiredException(string reason) : Exception(reason);

public enum RecoveryPhase { Retreating, Recovering, Ready }
public sealed record RetreatDecision(RecoveryPhase Phase, Vec? Waypoint = null);

// A separate escape rule is necessary: normal route planning rejects a start inside a circle.
// Every escape segment must move outward from ALL containing circles and avoid every other circle.
public static class RetreatPlanner
{
    public const double Clearance = 3;

    public static bool CanEscape(Vec start, Vec end, Vec anchor, double radius,
        IReadOnlyList<AvoidZone> zones, IReadOnlyList<RouteObstacle> obstacles)
    {
        if (!start.Finite || !end.Finite || !anchor.Finite || !double.IsFinite(radius) || radius <= 0 ||
            (start-anchor).Length > radius || (end-anchor).Length > radius ||
            !RoutePlanner.SegmentClear(start,end,obstacles)) return false;
        Vec step=end-start;
        if(step.Length<1e-6) return false;
        foreach(var zone in zones)
        {
            if(!zone.Center.Finite || !double.IsFinite(zone.Radius) || zone.Radius<0) return false;
            Vec offset=start-zone.Center;
            if(offset.Length<=zone.Radius+Clearance)
            {
                // Nonnegative initial derivative makes squared distance nondecreasing along the whole segment.
                if(offset.X*step.X+offset.Y*step.Y < -1e-9 || (end-zone.Center).Length<=offset.Length+1e-6) return false;
            }
            else if(Avoidance.BlockedSegment(start,end,[zone],Clearance)!=null) return false;
        }
        return true;
    }

    public static Vec? Step(Vec position, Vec forward, Vec anchor, double radius,
        IReadOnlyList<AvoidZone> zones, IReadOnlyList<RouteObstacle> obstacles)
    {
        Vec? best=null; double bestScore=double.NegativeInfinity;
        foreach(double distance in new[]{2.0,1.0,.5})
        for(int i=0;i<96;i++)
        {
            double angle=i*Math.PI*2/96;
            Vec direction=new(Math.Cos(angle),Math.Sin(angle)), end=position+direction*distance;
            if(!CanEscape(position,end,anchor,radius,zones,obstacles)) continue;
            // Include a small exit margin so a point exactly on the boundary still steps clear.
            double gain=zones.Sum(z=>Math.Max(0,z.Radius+Clearance+.1-(position-z.Center).Length)-
                Math.Max(0,z.Radius+Clearance+.1-(end-z.Center).Length));
            double score=gain+.01*(direction.X*forward.X+direction.Y*forward.Y);
            if(gain>1e-6 && score>bestScore) {best=end;bestScore=score;}
        }
        return best;
    }
}

public sealed class RetreatRecovery
{
    readonly long started;
    long? quietSince;
    public RecoveryPhase Phase {get;private set;}=RecoveryPhase.Retreating;
    public decimal HealthTarget {get;private set;}
    public RetreatRecovery(long now, decimal healBelow)
    {
        started=now;
        HealthTarget=Math.Min(95,Math.Max(1,healBelow)+10);
    }

    public void RequireFullHealth()
    {
        if(HealthTarget==100)return;
        HealthTarget=100;quietSince=null;
    }

    public RetreatDecision Update(long now, Vec position, Vec forward, Vec anchor, double radius,
        IReadOnlyList<AvoidZone> zones, IReadOnlyList<RouteObstacle> obstacles, Health hp)
    {
        if(!hp.Known || hp.Dead) throw new InvalidOperationException("Retreat stopped: player HP is unavailable or the character died.");
        if(now-started>=90000) throw new InvalidOperationException("Retreat/recovery stopped after 90 seconds without reaching a safe, recovered state.");
        if(Avoidance.BlockedPoint(position,zones,RetreatPlanner.Clearance)!=null)
        {
            quietSince=null; Phase=RecoveryPhase.Retreating;
            Vec? waypoint=RetreatPlanner.Step(position,forward,anchor,radius,zones,obstacles);
            if(waypoint==null) throw new RouteUnavailableException("Retreat stopped: no outward route within the hunt boundary avoids the known obstacles and other keep-away zones.");
            return new(Phase,waypoint);
        }
        Phase=RecoveryPhase.Recovering;
        bool recovered=hp.Current*100.0/hp.Maximum >= (double)HealthTarget;
        if(!recovered) quietSince=null;
        else quietSince??=now;
        if(quietSince.HasValue && now-quietSince.Value>=2000) Phase=RecoveryPhase.Ready;
        return new(Phase);
    }

    public static void SelfTest()
    {
        Vec anchor=new(0,0), start=new(2,0);
        AvoidZone[] zones=[new(new(0,0),5,"Knight")];
        if(!RetreatPlanner.CanEscape(start,new(4,0),anchor,20,zones,[]) ||
            RetreatPlanner.CanEscape(start,new(-4,0),anchor,20,zones,[])) throw new Exception("Escape must reject inward segments even when an endpoint is farther away.");
        if(RetreatPlanner.CanEscape(start,new(4,0),anchor,20,[..zones,new(new(6,0),1,"Other threat")],[])) throw new Exception("Escape moved deeper into an overlapping danger circle.");
        if(RetreatPlanner.CanEscape(new(8,0),new(10,0),anchor,9,zones,[])) throw new Exception("Escape crossed the hunt boundary.");
        var blocked=new[]{new RouteObstacle(new(3,0),.8,"Wall")};
        var step=RetreatPlanner.Step(start,new(1,0),anchor,20,zones,blocked);
        if(step==null || !RetreatPlanner.CanEscape(start,step.Value,anchor,20,zones,blocked)) throw new Exception("Escape failed to select a legal obstacle detour.");
        if(RetreatPlanner.CanEscape(new(0,0),new(10,0),anchor,20,[new(new(5,0),1,"Crossed threat")],[])) throw new Exception("Escape crossed an initially external zone.");
        if(RetreatPlanner.Step(new(0,0),new(1,0),anchor,20,[new(new(-2,0),5,"Left"),new(new(2,0),5,"Right"),new(new(0,-2),5,"Below"),new(new(0,2),5,"Above")],[])!=null) throw new Exception("Surrounded player must stop when every direction deepens another zone.");
        if(RetreatPlanner.Step(new(0,0),new(1,0),anchor,20,zones,[])==null) throw new Exception("Escape failed at a coincident threat center.");

        var recovery=new RetreatRecovery(0,75);
        Vec position=start;
        int moves=0;
        for(;moves<30;moves++)
        {
            var action=recovery.Update(moves*100,position,new(1,0),anchor,20,zones,[],new(60,100));
            if(action.Phase!=RecoveryPhase.Retreating) break;
            Vec next=action.Waypoint!.Value;
            if(!RetreatPlanner.CanEscape(position,next,anchor,20,zones,[])) throw new Exception("Replay escape segment violated a guard.");
            position=next;
        }
        if(moves==30 || recovery.Phase!=RecoveryPhase.Recovering) throw new Exception("Escape replay did not reach recovery.");
        var safe=new Vec(10,0);
        if(recovery.Update(5000,safe,new(1,0),anchor,20,zones,[],new(84,100)).Phase!=RecoveryPhase.Recovering) throw new Exception("Recovery resumed below target HP.");
        recovery.Update(6000,safe,new(1,0),anchor,20,zones,[],new(85,100));
        if(recovery.Update(7999,safe,new(1,0),anchor,20,zones,[],new(85,100)).Phase==RecoveryPhase.Ready) throw new Exception("Recovery skipped the quiet interval.");
        var movedThreat=new[]{new AvoidZone(new(4,0),5,"Knight moved")};
        if(recovery.Update(8000,safe,new(1,0),anchor,20,movedThreat,[],new(85,100)).Phase!=RecoveryPhase.Retreating) throw new Exception("Moving danger failed to restart retreat.");
        recovery.Update(9000,safe,new(1,0),anchor,20,zones,[],new(85,100));
        if(recovery.Update(11000,safe,new(1,0),anchor,20,zones,[],new(85,100)).Phase!=RecoveryPhase.Ready) throw new Exception("Recovery did not resume after stable safety and HP.");
        recovery.RequireFullHealth();
        if(recovery.Update(12000,safe,new(1,0),anchor,20,zones,[],new(85,100)).Phase==RecoveryPhase.Ready ||
            recovery.Update(13000,safe,new(1,0),anchor,20,zones,[],new(99,100)).Phase==RecoveryPhase.Ready)
            throw new Exception("Missing-item resting resumed at the old retreat percentage instead of full health.");
        recovery.Update(14000,safe,new(1,0),anchor,20,zones,[],new(100,100));recovery.RequireFullHealth();
        if(recovery.Update(16000,safe,new(1,0),anchor,20,zones,[],new(100,100)).Phase!=RecoveryPhase.Ready)
            throw new Exception("Full retreat recovery did not preserve its completed quiet interval.");
        foreach(var hp in new[]{new Health(0,100),default(Health)})
        {
            bool rejected=false;try{new RetreatRecovery(0,75).Update(0,safe,new(1,0),anchor,20,zones,[],hp);}catch(InvalidOperationException){rejected=true;}
            if(!rejected) throw new Exception("Dead/unknown HP allowed recovery.");
        }
        bool timedOut=false;try{recovery.Update(90000,safe,new(1,0),anchor,20,zones,[],new(50,100));}catch(InvalidOperationException){timedOut=true;}
        if(!timedOut)throw new Exception("Recovery was unbounded.");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"retreat-checks.json"),System.Text.Json.JsonSerializer.Serialize(new {Passed=true,EscapeReplaySteps=moves,RecoveryHealthTarget=recovery.HealthTarget,Checks=new[]{"outward-only segments","overlapping and external threat zones","hunt boundary","known obstacle detour","surrounded stop","coincident center","moving threat restarts retreat","HP and two-second stability","dead/unknown HP stop","90-second timeout"}},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
    }
}
