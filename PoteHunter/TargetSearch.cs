namespace PoteHunter;

public sealed record TargetSearchCandidate(uint Id,string Name,double Distance,double AnchorDistance,double Radius,string Reason);
public sealed record TargetSearchReport(string Message,int ApprovedAlive,int OutsideArea,int Unreadable,int Protected,TargetSearchCandidate[] Candidates);

public static class TargetSearch
{
    public static TargetSearchReport Explain(IEnumerable<Entity> entities,IReadOnlyDictionary<uint,Health> health,
        Vec position,Vec anchor,double radius,double responseRadius,Func<Entity,Threat> difficulty,
        string filter,IReadOnlyCollection<string> colors,bool prioritizeGamekeeper,Func<Entity,Health,string?> protection)
    {
        var candidates=new List<TargetSearchCandidate>();
        foreach(var entity in entities.Where(e=>e.Targetable && e.Position.Finite))
        {
            // Use the same approval rules as selection, separately from HP and the hunt boundary.
            if(!Targeting.Eligible(entity,new Health(1,1),difficulty(entity),filter,colors,prioritizeGamekeeper))continue;
            var hp=health.GetValueOrDefault(entity.Id);
            if(hp.Dead)continue;
            double limit=Targeting.TargetRadius(entity,prioritizeGamekeeper,radius,responseRadius);
            double distance=(entity.Position-position).Length,fromAnchor=(entity.Position-anchor).Length;
            string reason=!hp.Known ? "HP unavailable" : fromAnchor>limit ? "Outside hunting area" : protection(entity,hp) ?? "Available";
            candidates.Add(new(entity.Id,entity.DisplayName,distance,fromAnchor,limit,reason));
        }
        var ordered=candidates.OrderBy(e=>e.Distance).ToArray();
        int unknown=ordered.Count(e=>e.Reason=="HP unavailable"),outside=ordered.Count(e=>e.Reason=="Outside hunting area");
        int blocked=ordered.Count(e=>e.Reason is not ("Available" or "Outside hunting area" or "HP unavailable"));
        string message;
        var nearestOutside=ordered.FirstOrDefault(e=>e.Reason=="Outside hunting area");
        if(ordered.Any(e=>e.Reason=="Available"))message="Refreshing approved target selection…";
        else if(blocked>0)message="Waiting: "+ordered.First(e=>e.Reason is not ("Outside hunting area" or "HP unavailable")).Reason+".";
        else if(unknown>0)message=$"Waiting for HP readings on {unknown} approved target(s).";
        else if(nearestOutside!=null)message=$"Waiting: approved targets are outside the hunting area. Nearest: {nearestOutside.AnchorDistance:F1} from center; limit {nearestOutside.Radius:F0}.";
        else message=$"Waiting: no living targets match your selection within the loaded area ({string.Join(", ",colors)}{(filter.Length>0 ? "; "+filter : "")}).";
        return new(message,ordered.Length-unknown,outside,unknown,blocked,ordered.Take(30).ToArray());
    }

    public static void SelfTest()
    {
        var green=new Entity(100,0x80000001,"Lv. 1 Approved",new(21,0),0);
        var yellow=new Entity(200,0x80000002,"Lv. 2 Other",new(5,0),0);
        var hp=new Dictionary<uint,Health>{{green.Id,new(100,100)},{yellow.Id,new(100,100)}};
        TargetSearchReport Check(Entity target,Func<Entity,Health,string?>? guard=null)=>Explain([target,yellow],hp,new(19,0),new(0,0),20,60,
            e=>e.Id==green.Id?Threat.Green:Threat.Yellow,"",["Green"],true,guard??((_,_)=>null));
        var outside=Check(green);
        if(outside.OutsideArea!=1 || outside.Candidates.Length!=1 || outside.Candidates[0].Distance!=2 || outside.Candidates[0].AnchorDistance!=21)
            throw new Exception("Search diagnostics confused player distance with hunt-center distance or treated an unapproved monster as an obstacle.");
        var inside=green with{Position=new(10,0)};
        if(Check(inside).Candidates[0].Reason!="Available")throw new Exception("A clear approved target was reported blocked.");
        if(Check(inside,(_,_)=>"Player nearby").Protected!=1)throw new Exception("Search diagnostics lost a real target protection.");
        hp.Remove(green.Id);
        if(Check(inside).Unreadable!=1)throw new Exception("Missing HP was reported as an obstruction.");
        hp[green.Id]=new(0,100);
        if(Check(inside).Candidates.Length!=0)throw new Exception("Dead or unapproved monsters were reported as live approved targets.");
    }
}
