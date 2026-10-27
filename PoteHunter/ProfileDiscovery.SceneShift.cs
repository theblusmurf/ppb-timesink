using System.Security.Cryptography;
using System.Text.Json;

namespace PoteHunter;

public static partial class ProfileDiscovery
{
    // This is a bounded relocation of a proved instruction/data shape, not a
    // guessed layout. Every other root, class, stride and member check remains
    // mandatory. Independently used scene members must all move together.
    static (Rule Rule,int[] Offsets,bool Slots)[] SceneShiftFields()
    {
        var catalog=october18Signatures.Value;
        var scene=catalog.Roots.Where(r=>r.Name=="Scene" && r.CaptureOffset==2).ToArray();
        return [
            (scene[0],[8,21,35],false),(scene[1],[16],false),
            (catalog.Checks.Single(r=>r.Name=="Hotbar slots and page"),[2],false),
            (catalog.Checks.Single(r=>r.Name=="Ground list sentinel"),[2],false),
            (catalog.Checks.Single(r=>r.Name=="Ground list count"),[10],false),
            (catalog.Checks.Single(r=>r.Name=="Hotbar slots and page"),[11],true),
            (catalog.Checks.Single(r=>r.Name=="Selected hotbar slot"),[2],true)
        ];
    }

    static Rule MaskSceneMembers(Rule rule,IEnumerable<int> offsets)
    {
        var tokens=rule.Pattern.Split(' ');
        foreach(int offset in offsets)for(int i=0;i<4;i++)tokens[offset+i]="??";
        return rule with {Pattern=string.Join(' ',tokens)};
    }

    static Rule FreezeMembers(Rule rule,PeImage image,uint location,IEnumerable<int> offsets)
    {
        var tokens=rule.Pattern.Split(' ');
        foreach(int offset in offsets)
        {
            var value=BitConverter.GetBytes(image.U32Rva(location+(uint)offset));
            for(int i=0;i<4;i++)tokens[offset+i]=value[i].ToString("X2");
        }
        return rule with {Pattern=string.Join(' ',tokens)};
    }

    static ProfileDetection DiscoverShiftedScene(byte[] bytes)
    {
        var image=new PeImage(bytes);
        var fields=SceneShiftFields();
        var adapted=new Dictionary<string,Rule>(StringComparer.Ordinal);
        long? sceneShift=null,slotShift=null;
        foreach(var group in fields.GroupBy(f=>f.Rule.Pattern))
        {
            var rule=group.First().Rule;
            int[] offsets=group.SelectMany(f=>f.Offsets).ToArray();
            var masked=MaskSceneMembers(rule,offsets);
            var found=image.Find(new Pattern(masked.Pattern));
            byte[] original=rule.Pattern.Split(' ').Select(t=>t=="??"?(byte)0:Convert.ToByte(t,16)).ToArray();
            Require(found.Count is >0 and <=16,"missing or overly broad scene relocation proof: "+rule.Name);
            // Shared UI handlers can have identical instructions for different
            // fields. A prior independent consumer must disambiguate them;
            // never choose by first match or by a plausible live value.
            found=found.Where(location=>group.All(field=>field.Offsets.All(offset=>{
                long delta=(long)image.U32Rva(location+(uint)offset)-BitConverter.ToUInt32(original,offset);
                var expected=field.Slots?slotShift:sceneShift;
                return Math.Abs(delta)<=0x800 && (delta&3)==0 && (!expected.HasValue || expected.Value==delta);
            }))).ToList();
            Require(found.Count==1,"missing or ambiguous coherent scene relocation proof: "+rule.Name);
            foreach(var field in group)
                foreach(int offset in field.Offsets)
                {
                    uint value=image.U32Rva(found[0]+(uint)offset);
                    long delta=(long)value-BitConverter.ToUInt32(original,offset);
                    Require(Math.Abs(delta)<=0x800 && (delta&3)==0,"scene relocation outside supported bounds: "+rule.Name);
                    var expected=field.Slots?slotShift:sceneShift;
                    Require(!expected.HasValue || expected.Value==delta,"independent scene members disagree: "+rule.Name);
                    if(field.Slots)slotShift=delta;else sceneShift=delta;
                }
            // The live-image check must verify the exact derived operands too.
            adapted[rule.Pattern]=FreezeMembers(rule,image,found[0],offsets);
        }
        Require(sceneShift.HasValue && slotShift.HasValue,"incomplete scene relocation proof");
        var baseline=SceneLayout.October18;
        uint Shift(uint member,long delta)=>checked((uint)(member+delta));
        var layout=baseline with {
            HotbarPage=Shift(baseline.HotbarPage,sceneShift!.Value),
            GroundItems=Shift(baseline.GroundItems,sceneShift.Value),
            GroundCount=Shift(baseline.GroundCount,sceneShift.Value),
            HotbarSlots=Shift(baseline.HotbarSlots,slotShift!.Value),
            SelectedSlot=Shift(baseline.SelectedSlot,slotShift.Value)
        };
        Require(layout.HotbarSlots>=0x1000 && layout.HotbarSlots+0x400<layout.GroundItems &&
            layout.GroundCount+4<=layout.HotbarPage,"derived scene arrays overlap or are outside bounds");
        var catalog=october18Signatures.Value;
        Rule Adapt(Rule rule)=>adapted.TryGetValue(rule.Pattern,out var replacement)?replacement:rule;
        return Discover(bytes,new Manifest(catalog.Version,catalog.Roots.Select(Adapt).ToArray(),catalog.Checks.Select(Adapt).ToArray()),layout);
    }

    static void CheckShiftedSceneFixture(byte[] fixture,Dictionary<string,int> locations,Dictionary<string,uint> roots)
    {
        byte[] Shift(int sceneDelta,int slotDelta)
        {
            byte[] copy=(byte[])fixture.Clone();
            foreach(var field in SceneShiftFields())
                foreach(int offset in field.Offsets)
                {
                    int at=locations[field.Rule.Pattern]+offset;
                    BitConverter.TryWriteBytes(copy.AsSpan(at,4),checked((uint)(BitConverter.ToUInt32(fixture,at)+(field.Slots?slotDelta:sceneDelta))));
                }
            return copy;
        }
        void Reject(byte[] broken,string label)
        {
            bool rejected=false;try {ResolveForConnection(broken);}catch(InvalidOperationException){rejected=true;}
            if(!rejected || PoteMemoryProbe.ClientCompatibility.SupportsRead(Convert.ToHexStringLower(SHA256.HashData(broken))))
                throw new Exception("Scene relocation accepted "+label);
        }
        foreach(var delta in new[]{-0x40,0x80,0x180})
        {
            byte[] shifted=Shift(delta,0x40);
            var detected=Discover(shifted);
            if(detected.Profile.Scene!=roots["Scene"] || detected.Profile.Layout.HotbarPage!=SceneLayout.October18.HotbarPage+delta ||
                detected.Profile.Layout.HotbarSlots!=SceneLayout.October18.HotbarSlots+0x40 || detected.Profile.Layout.Effects!=0)
                throw new Exception("Coherent scene relocation was not rediscovered");
            var proof=detected.Evidence.Single(e=>e.Name=="Hotbar slots and page");
            var image=new PeImage(shifted);int at=image.FileOffset(proof.CodeRva);
            byte[] changed=shifted.AsSpan(at,proof.Pattern.Split(' ').Length).ToArray();changed[2]^=4;
            if(Matches(changed,proof.Pattern))throw new Exception("Loaded scene operands were not pinned to the discovered values");
        }
        byte[] valid=Shift(0x80,0);
        foreach(var field in SceneShiftFields())
            foreach(int offset in field.Offsets)
            {
                byte[] mixed=(byte[])valid.Clone();mixed[locations[field.Rule.Pattern]+offset]^=4;Reject(mixed,"one conflicting field");
            }
        Reject(Shift(0x804,0),"an excessive shift");Reject(Shift(2,0),"unaligned members");
        byte[] missing=(byte[])valid.Clone();missing[locations[october18Signatures.Value.Checks.Single(r=>r.Name=="Live position fields").Pattern]]=0xcc;
        Reject(missing,"a missing non-relocatable position proof");
        var ground=SceneShiftFields().Single(f=>f.Rule.Name=="Ground list sentinel");
        byte[] ambiguous=(byte[])valid.Clone();int length=ground.Rule.Pattern.Split(' ').Length;
        valid.AsSpan(locations[ground.Rule.Pattern],length).CopyTo(ambiguous.AsSpan(0x6800));Reject(ambiguous,"ambiguous member code");
        var selected=SceneShiftFields().Single(f=>f.Rule.Name=="Selected hotbar slot");
        byte[] unrelated=(byte[])valid.Clone();
        valid.AsSpan(locations[selected.Rule.Pattern],selected.Rule.Pattern.Split(' ').Length).CopyTo(unrelated.AsSpan(0x6800));
        BitConverter.TryWriteBytes(unrelated.AsSpan(0x6802,4),SceneLayout.October18.SelectedSlot+0x40);
        if(Discover(unrelated).Profile.Layout.SelectedSlot!=SceneLayout.October18.SelectedSlot)
            throw new Exception("Independent hotbar consumer did not disambiguate another UI field");
        BitConverter.TryWriteBytes(unrelated.AsSpan(0x6802,4),SceneLayout.October18.SelectedSlot);
        Reject(unrelated,"two coherent selected-slot consumers");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"scene-relocation-checks.json"),JsonSerializer.Serialize(new {
            Passed=true,HardwareInputEmitted=false,Checks=new[]{"coherent negative and positive scene shifts","independent slot shift","all existing root and layout proofs retained","each inconsistent operand rejected","oversized and unaligned shifts rejected","ambiguous consumers rejected","missing position proof rejected","loaded operands pinned","unsupported effect observer stays disabled"}}));
    }
}
