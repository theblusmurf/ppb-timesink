namespace PoteHunter;

public sealed record CameraLayoutEvidence(
    uint ViewCameraGlobalRva,
    uint ViewCameraVtableRva,
    uint RootCodeRva,
    string RootPattern,
    uint ConstructorCodeRva,
    string ConstructorPattern,
    int ViewMatrixOffset = 0x1E0,
    int InverseViewMatrixOffset = 0x220,
    int HorizontalAspectOffset = 0x1C4,
    int HorizontalFovOffset = 0x1C8);

public static class CameraDiscovery
{
    // CCameraControl::SetCamera writes the inverse-view and view matrices through
    // CSceneManager::m_ViewCamera. The two loads of the same static pointer and
    // the four 16-byte matrix stores make this independent of a fixed global RVA.
    internal const string RootPattern =
        "A1 ?? ?? ?? ?? 0F 11 80 20 02 00 00 0F 10 84 24 90 00 00 00 " +
        "0F 11 80 30 02 00 00 0F 10 84 24 A0 00 00 00 0F 11 80 40 02 00 00 " +
        "0F 10 84 24 B0 00 00 00 0F 11 80 50 02 00 00 A1 ?? ?? ?? ?? " +
        "0F 10 44 24 40 0F 11 80 E0 01 00 00";

    // CViewCamera constructor: vtable, position vector, view/inverse matrices,
    // then the three rotation fields. This proves the member layout per build.
    internal const string ConstructorPattern =
        "C7 06 ?? ?? ?? ?? C7 86 B0 00 00 00 00 00 00 00 " +
        "C7 86 B4 00 00 00 00 00 00 00 C7 86 B8 00 00 00 00 00 00 00 " +
        "8D 8E E0 01 00 00 C7 45 FC 00 00 00 00 C7 46 10 00 00 00 00 " +
        "E8 ?? ?? ?? ?? 8D 8E 20 02 00 00 E8 ?? ?? ?? ?? " +
        "C7 86 C0 01 00 00 00 00 00 00 8B C6 " +
        "C7 86 BC 01 00 00 00 00 00 00 C7 86 B8 01 00 00 00 00 00 00";

    public static CameraLayoutEvidence? Resolve(string clientPath)
    {
        var root=ProfileDiscovery.OptionalEvidence(clientPath,"View camera static root and matrices",RootPattern);
        var ctor=ProfileDiscovery.OptionalEvidence(clientPath,"View camera constructor layout",ConstructorPattern);
        if(root==null||ctor==null)return null;
        byte[] bytes=File.ReadAllBytes(clientPath);
        var pe=new PeReader(bytes);
        uint rootVa=pe.U32AtRva(checked(root.CodeRva+1));
        uint vtableVa=pe.U32AtRva(checked(ctor.CodeRva+2));
        if(rootVa<pe.ImageBase||vtableVa<pe.ImageBase)return null;
        uint rootRva=rootVa-pe.ImageBase,vtableRva=vtableVa-pe.ImageBase;
        if(rootRva>=pe.ImageSize||vtableRva>=pe.ImageSize)return null;
        if(!pe.Writable(rootRva)||!pe.FileBackedReadOnly(vtableRva)||!pe.Executable(root.CodeRva)||!pe.Executable(ctor.CodeRva))return null;
        return new(rootRva,vtableRva,root.CodeRva,RootPattern,ctor.CodeRva,ConstructorPattern);
    }

    sealed class PeReader
    {
        readonly byte[] bytes;
        readonly (uint Rva,uint VirtualSize,uint Raw,uint RawSize,uint Flags)[] sections;
        public uint ImageBase{get;}
        public uint ImageSize{get;}
        uint U32(int offset)=>BitConverter.ToUInt32(bytes,offset);
        ushort U16(int offset)=>BitConverter.ToUInt16(bytes,offset);
        public PeReader(byte[] bytes)
        {
            this.bytes=bytes;
            int pe=checked((int)U32(0x3C));
            if(U32(pe)!=0x4550||U16(pe+4)!=0x14C||U16(pe+24)!=0x10B)throw new InvalidOperationException("Camera discovery requires the verified PE32 x86 client.");
            int count=U16(pe+6),optional=U16(pe+20),table=pe+24+optional;
            ImageBase=U32(pe+24+28);ImageSize=U32(pe+24+56);
            sections=Enumerable.Range(0,count).Select(i=>
            {
                int p=table+i*40;
                return (U32(p+12),Math.Max(U32(p+8),U32(p+16)),U32(p+20),U32(p+16),U32(p+36));
            }).ToArray();
        }
        public uint U32AtRva(uint rva)
        {
            foreach(var s in sections)
                if(s.RawSize>=4&&rva>=s.Rva&&rva-s.Rva<=s.RawSize-4)return U32(checked((int)(s.Raw+rva-s.Rva)));
            throw new InvalidOperationException("Camera signature capture is outside file-backed client data.");
        }
        (uint Rva,uint VirtualSize,uint Raw,uint RawSize,uint Flags)? SectionAt(uint value,bool root)
        {
            uint rva=value>=ImageBase?value-ImageBase:value;
            foreach(var s in sections)if(rva>=s.Rva&&(ulong)(rva-s.Rva)<s.VirtualSize)return s;
            if(root)throw new InvalidOperationException("Camera discovery resolved outside the client image.");
            return null;
        }
        public bool Writable(uint rva)=>(SectionAt(rva,true)!.Value.Flags&0x80000000)!=0;
        public bool Executable(uint rva)=>(SectionAt(rva,true)!.Value.Flags&0x20000000)!=0;
        public bool FileBackedReadOnly(uint rva)
        {
            var s=SectionAt(rva,true)!.Value;
            return s.RawSize>0&&(s.Flags&0x80000000)==0&&(s.Flags&0x40000000)!=0;
        }
    }
}
