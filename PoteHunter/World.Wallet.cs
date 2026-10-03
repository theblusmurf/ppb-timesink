using PoteMemoryProbe;

namespace PoteHunter;

public readonly record struct WalletReading(bool Known,uint Current,string Character,DateTime ObservedUtc,string Status);

public sealed partial class World
{
    int walletOffset=-1;
    public string WalletStatus { get; private set; }="Wallet layout has not been checked";

    public void ConfigureWallet()
    {
        ResetWallet();
        if(!ConnectionVerified || handle is null || moduleBase==0)return;
        try
        {
            var evidence=ProfileDiscovery.WalletEvidence(PoteMemoryProbe.Program.ClientPath,profile.Scene);
            if(evidence.Count!=4){WalletStatus="Wallet display signatures were missing, ambiguous or inconsistent";return;}
            var code=evidence.Select(e=>Native.Read(handle,(nint)(moduleBase+e.CodeRva),e.Pattern.Split(' ').Length)).ToArray();
            for(int i=0;i<evidence.Count;i++)
                if(!ProfileDiscovery.Matches(code[i],evidence[i].Pattern))throw new InvalidOperationException("Loaded wallet code differs from the client file");
            if(BitConverter.ToUInt32(code[0],2)!=moduleBase+profile.Scene || BitConverter.ToUInt32(code[2],2)!=moduleBase+profile.Scene)
                throw new InvalidOperationException("Loaded wallet scene roots do not match the connected profile");
            uint first=BitConverter.ToUInt32(code[1],8),second=BitConverter.ToUInt32(code[3],8);
            if(first!=second || first is <0x100 or >0x400)throw new InvalidOperationException("Wallet display fields disagree");
            walletOffset=(int)first;
            WalletStatus="Wallet balance validated against both inventory display routines";
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {walletOffset=-1;WalletStatus="Wallet unavailable: "+ex.Message;}
    }

    void ResetWallet(){walletOffset=-1;WalletStatus="Wallet layout is not enabled";}

    public WalletReading ReadWallet()
    {
        WalletReading Unknown(string reason)=>new(false,0,"",DateTime.UtcNow,reason);
        if(!ConnectionVerified || handle is null || walletOffset<0)return Unknown(WalletStatus);
        try
        {
            var self=LocalPlayer();uint scene=Pointer(moduleBase+profile.Scene);
            if(scene<0x10000)return Unknown("Waiting for the wallet scene");
            uint first=Pointer(scene+walletOffset),second=Pointer(scene+walletOffset);
            if(first!=second || scene!=Pointer(moduleBase+profile.Scene) || !LocalCharacter.Same(self,LocalPlayer()))
                return Unknown("Wallet changed during the read; waiting for a stable sample");
            return new(true,first,self.Name,DateTime.UtcNow,WalletStatus);
        }
        catch(Exception ex) when(ex is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
        {return Unknown("Wallet read unavailable: "+ex.Message);}
    }
}
