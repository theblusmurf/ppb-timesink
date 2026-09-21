using System.Text;

namespace PoteHunter;

public sealed record PartyMember(uint Id,string Name,bool Leader);
public sealed record PartySnapshot(bool Available,IReadOnlyList<PartyMember> Members,string Status)
{
    public static PartySnapshot Parse(byte[] scene,uint selfId)
    {
        if(scene.Length<0x30d)throw new InvalidOperationException("Party snapshot is incomplete.");
        int count=scene[0x244];
        if(count>10)throw new InvalidOperationException("Party member count is invalid.");
        if(count==0)return new(true,[],"Not in a party");
        uint leader=BitConverter.ToUInt32(scene,0x240);
        var members=new List<PartyMember>();
        for(int i=0;i<count;i++)
        {
            uint id=BitConverter.ToUInt32(scene,0x2e5+i*4);
            var nameBytes=scene.AsSpan(0x245+i*16,16);
            int end=nameBytes.IndexOf((byte)0);
            string name=new UTF8Encoding(false,true).GetString(end<0?nameBytes:nameBytes[..end]);
            if(id==0 || (id&0xf0000000)!=0 || string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl) ||
                members.Any(m=>m.Id==id || m.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Party identity data is changing or invalid.");
            members.Add(new(id,name,id==leader));
        }
        if(!members.Any(m=>m.Id==selfId) || !members.Any(m=>m.Leader))throw new InvalidOperationException("Party membership is changing; waiting for a consistent roster.");
        return new(true,members,$"{count} party members");
    }
    public static void SelfTest()
    {
        var bytes=new byte[0x30d];bytes[0x244]=3;
        BitConverter.GetBytes(3420u).CopyTo(bytes,0x240);
        string[] names=["E-Thug","Domitus","Gimp"];uint[] ids=[3420,4027,3447];
        for(int i=0;i<3;i++){Encoding.UTF8.GetBytes(names[i]).CopyTo(bytes,0x245+i*16);BitConverter.GetBytes(ids[i]).CopyTo(bytes,0x2e5+i*4);}
        var party=Parse(bytes,3447);
        if(party.Members.Count!=3 || party.Members[1].Name!="Domitus" || party.Members[1].Id!=4027 || !party.Members[0].Leader)throw new Exception("Party name/UID arrays were misaligned.");
        bytes[0x244]=11;bool rejected=false;try{Parse(bytes,3447);}catch(InvalidOperationException){rejected=true;}
        if(!rejected)throw new Exception("Invalid party count was accepted.");
        bytes[0x244]=0;if(Parse(bytes,3447).Members.Count!=0)throw new Exception("Party departure retained old members.");
    }
}
