using System.Numerics;

namespace PoteHunter;

public static class RangedTargetVisibilityChecks
{
    public static void RunAll()
    {
        var camera=new CameraFrame(new(0,1,-10),Vector3.UnitZ,Vector3.UnitY,MathF.PI/2,16f/9f);
        Entity target=Monster(10,0,10,0),front=Monster(20,0,0,0),nearer=Monster(30,0,-4,0);
        var blocked=RangedTargetVisibility.FindFrontBlocker(camera,target,[front,nearer]);
        Expect(blocked.Blocker==nearer&&blocked.EntryDistance<10,"nearest front monster");
        Expect(!RangedTargetVisibility.FindFrontBlocker(camera,target,[target]).Blocked,"identity exclusion");
        Expect(!RangedTargetVisibility.FindFrontBlocker(camera,target,[Monster(40,0,15,0)]).Blocked,"behind intended target");
        Expect(!RangedTargetVisibility.FindFrontBlocker(camera,target,[Monster(41,0,10.2,0)]).Blocked,"overlapping body with center behind intended target");
        Entity slightlyFartherTarget=Monster(42,0,10.2,0);
        Expect(RangedTargetVisibility.FindFrontBlocker(camera,slightlyFartherTarget,[target]).Blocker==target,"overlapping body with center in front of intended target");
        Expect(!RangedTargetVisibility.FindFrontBlocker(camera,target,[Monster(50,0,-12,0)]).Blocked,"behind camera eye");
        Expect(!RangedTargetVisibility.FindFrontBlocker(camera,target,[Monster(60,.51,0,0)]).Blocked,"horizontal separation");
        Expect(RangedTargetVisibility.FindFrontBlocker(camera,target,[Monster(61,.49,0,0)]).Blocked,"body-edge intersection");
        Expect(!RangedTargetVisibility.FindFrontBlocker(camera,target,[Monster(70,0,0,2.01)]).Blocked,"vertical separation");
        // Policy approval is intentionally absent: a valid loaded monster blocks
        // even when the caller would decline to attack it.
        Entity unapproved=Monster(80,0,2,0) with{Name="Lv. Avoided monster"};
        Expect(RangedTargetVisibility.FindFrontBlocker(camera,target,[unapproved]).Blocker==unapproved,"unapproved monster still blocks");
        Entity invalid=Monster(90,double.NaN,0,0);
        Expect(!RangedTargetVisibility.FindFrontBlocker(camera,target,[invalid]).Blocked,"invalid blocker ignored");
    }

    static Entity Monster(uint id,double x,double z,double height)=>new(0x10000+id,0x80000000|id,"Lv. Monster",new(x,z),height);
    static void Expect(bool condition,string name)
    {
        if(!condition)throw new InvalidOperationException("Ranged target visibility check failed: "+name);
    }
}
