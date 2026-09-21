namespace PoteHunter;

public static class LocalCharacter
{
    public static Entity Require(uint localId,Entity? candidate)
    {
        if(localId==0 || (localId&0xf0000000)!=0 || candidate==null || candidate.Id!=localId ||
            !candidate.Model.StartsWith("PC_",StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(candidate.Name))
            throw new InvalidOperationException("Waiting for a logged-in character to enter the world.");
        return candidate;
    }
    public static bool Same(Entity a,Entity b) => a.Id==b.Id && a.Generation==b.Generation && a.Address==b.Address && a.Name==b.Name;

    public static void SelfTest()
    {
        var player=new Entity(100,3447,"Gimp",new(1,1),0,Generation:1,Model:"PC_Akhan_A.GCMDS");
        var changed=new Entity(200,4028,"DIeseL",new(1,1),0,Generation:2,Model:"PC_MAN.GCMDS");
        if(Require(player.Id,player)!=player || Require(changed.Id,changed).Name!="DIeseL" || Same(player,changed) ||
            !Same(player,player with{Position=new(8,9)}) || !Same(player,player with{Height=player.Height+.5}) ||
            Same(player,player with{Generation=2}) || Same(player,player with{Address=101}))
            throw new Exception("Automatic character identification or run identity checks failed");
        foreach(var invalid in new Entity?[]{null,player with{Name=""},player with{Id=9},player with{Model="NPC_A.GCMDS"}})
        {
            bool rejected=false;try{Require(player.Id,invalid);}catch(InvalidOperationException){rejected=true;}
            if(!rejected)throw new Exception("Invalid local character was accepted");
        }
        bool highUidRejected=false;try{Require(0x80001753,player with{Id=0x80001753});}catch(InvalidOperationException){highUidRejected=true;}
        if(!highUidRejected)throw new Exception("Monster-class UID accepted as local character");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"character-detection-checks.json"),System.Text.Json.JsonSerializer.Serialize(new {
            Passed=true,Checks=new[]{"name from local identity","Gimp to DIeseL change","movement preserves identity","reused allocation/generation rejected","blank/inactive characters rejected","NPC/monster/wrong UID rejected"}},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
    }
}
