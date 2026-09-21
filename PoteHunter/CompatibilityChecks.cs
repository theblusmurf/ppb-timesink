using PoteMemoryProbe;

namespace PoteHunter;

internal static class CompatibilityChecks
{
    internal static int CheckPatchedFile(string path)
    {
        try
        {
            byte[] original=File.ReadAllBytes(path);
            var current=ProfileDiscovery.ResolveForConnection(original);
            // Appending an unused PE overlay changes the hash without changing
            // the mapped layout. Work on memory only; never write the game file.
            byte[] patched=new byte[original.Length+1];original.CopyTo(patched,0);patched[^1]=0x5a;
            var changed=ProfileDiscovery.ResolveForConnection(patched);
            if(current.Profile.Sha256==changed.Profile.Sha256 ||
                !current.Evidence.SequenceEqual(changed.Evidence) || !ClientCompatibility.SupportsRead(changed.Profile.Sha256) ||
                WindowsClientInput.ConnectionBlockReason(true,true,changed.Profile.Sha256)!=null ||
                WindowsClientInput.ConnectionBlockReason(true,false,changed.Profile.Sha256)==null)
                throw new Exception("An unchanged-layout patch failed automatic admission or skipped live verification.");
            // Destroy a mandatory member signature and check it receives no approval.
            byte[] broken=(byte[])original.Clone();
            using var pe=new System.Reflection.PortableExecutable.PEReader(new MemoryStream(original));
            var proof=current.Evidence.First(e=>e.Name=="Hotbar slots and page");
            var section=pe.PEHeaders.SectionHeaders.Single(s=>proof.CodeRva>=s.VirtualAddress && proof.CodeRva<s.VirtualAddress+s.SizeOfRawData);
            broken[checked((int)proof.CodeRva-section.VirtualAddress+section.PointerToRawData)]=0xcc;
            bool rejected=false;
            try{ProfileDiscovery.ResolveForConnection(broken);}catch(InvalidOperationException){rejected=true;}
            string brokenHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(broken));
            if(!rejected || ClientCompatibility.SupportsRead(brokenHash))throw new Exception("A broken layout was approved.");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"patch-compatibility-check.json"),System.Text.Json.JsonSerializer.Serialize(new
            {
                Passed=true,CurrentHash=current.Profile.Sha256,SimulatedPatchHash=changed.Profile.Sha256,
                SignatureCount=current.Evidence.Count,UnchangedLayoutAccepted=true,BrokenLayoutRejected=true,
                LiveValidationStillRequired=true,ClientFilesModified=false,HardwareInputEmitted=false
            }));
            return 0;
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"patch-compatibility-check.json"),System.Text.Json.JsonSerializer.Serialize(new{Passed=false,Error=ex.ToString()}));
            return 1;
        }
    }

    internal static void Run()
    {
        string current=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray()));
        if(ClientCompatibility.SupportsRead(current) || WindowsClientInput.ConnectionBlockReason(true,true,current)==null)
            throw new Exception("An undiscovered patch was admitted without validation.");
        ClientCompatibility.AuthorizeDiscoveredRead(current);
        foreach(string hash in new[]{current,current.ToUpperInvariant(),current.ToLowerInvariant(),"08b039fcc0930fabff4004afac611a66e58067c7c76405953791a22800d7fb53","510cd56cfee8b853ccd4da8defca924206120c5977509b43f11158aa318d5871"})
            if(!ClientCompatibility.SupportsRead(hash) || WindowsClientInput.ConnectionBlockReason(true,true,hash)!=null)
                throw new Exception("Reader and input approvals disagree for a reviewed client.");
        foreach(string hash in new[]{"",new string('0',64)})
            if(ClientCompatibility.SupportsRead(hash) || WindowsClientInput.ConnectionBlockReason(true,true,hash)==null)
                throw new Exception("An unknown client was admitted to compatibility input.");
        if(WindowsClientInput.ConnectionBlockReason(false,true,current)==null || WindowsClientInput.ConnectionBlockReason(true,false,current)==null)
            throw new Exception("Compatibility input bypassed reader opt-in or completed connection validation.");
        using var world=new World();
        if(world.ConnectionVerified)throw new Exception("A newly created world was reported as verified.");
        world.Dispose();
        if(world.ConnectionVerified)throw new Exception("A disposed world retained connection verification.");
        byte[] malformed=new byte[512];
        string malformedHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(malformed));
        bool rejected=false;
        try{ProfileDiscovery.ResolveForConnection(malformed);}catch(InvalidOperationException){rejected=true;}
        if(!rejected || ClientCompatibility.SupportsRead(malformedHash))throw new Exception("Failed discovery granted a compatibility-read approval.");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"client-compatibility-checks.json"),System.Text.Json.JsonSerializer.Serialize(new
        {
            Passed=true,HardwareInputEmitted=false,Checks=new[]{"session-discovered patches accepted","previous read approvals retained","case-insensitive hashes","undiscovered files rejected","failed discovery grants no approval","read opt-in required","completed live validation required","fresh/disposed world not verified"}
        }));
    }
}
