using System.Buffers.Binary;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PoteHunter;

// Narrow DDS decoder for the client's 256x256 UI maps. No game writes or third
// party codecs. Reject unsupported formats, masks, dimensions and truncated data.
internal static class DdsMapTile
{
    internal static Bitmap Decode(byte[] data)
    {
        uint U(int at)=>BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at,4));
        if(data.Length<128 || U(0)!=0x20534444 || U(4)!=124 || U(12)!=256 || U(16)!=256 || U(76)!=32)
            throw new InvalidDataException("Invalid map DDS header");
        uint format=U(84);int blockBytes=format==0x31545844?8:16;
        bool compressed=(U(80)&4)!=0;
        if(compressed && format is not (0x31545844 or 0x33545844 or 0x35545844))throw new InvalidDataException("Unsupported map DDS compression");
        if(!compressed && ((U(80)&64)==0 || U(88)!=32 || U(92)!=0xff0000 || U(96)!=0xff00 || U(100)!=0xff || U(104)!=0xff000000 || U(20)!=1024))
            throw new InvalidDataException("Unsupported map DDS pixel layout");
        int required=compressed?64*64*blockBytes:256*256*4;
        if(data.Length<128+required)throw new InvalidDataException("Truncated map DDS");
        var pixels=new byte[256*256*4];
        if(!compressed)Buffer.BlockCopy(data,128,pixels,0,pixels.Length);
        else
        {
            for(int by=0;by<64;by++)for(int bx=0;bx<64;bx++)
            {
                int at=128+(by*64+bx)*blockBytes,colorAt=at+(blockBytes==8?0:8);
                ushort c0=BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(colorAt,2)),c1=BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(colorAt+2,2));
                var palette=new (int R,int G,int B)[4];
                static (int,int,int) Color565(ushort c)=>(((c>>11)&31)*255/31,((c>>5)&63)*255/63,(c&31)*255/31);
                palette[0]=Color565(c0);palette[1]=Color565(c1);
                bool opaque=format!=0x31545844 || c0>c1;
                for(int channel=0;channel<3;channel++)
                {
                    int a=channel==0?palette[0].R:channel==1?palette[0].G:palette[0].B,b=channel==0?palette[1].R:channel==1?palette[1].G:palette[1].B;
                    int c=opaque?(2*a+b)/3:(a+b)/2,d=opaque?(a+2*b)/3:0;
                    if(channel==0){palette[2].R=c;palette[3].R=d;}else if(channel==1){palette[2].G=c;palette[3].G=d;}else{palette[2].B=c;palette[3].B=d;}
                }
                uint indices=U(colorAt+4);ulong alphaBits=0;var alpha=new int[8];
                if(format==0x35545844)
                {
                    alpha[0]=data[at];alpha[1]=data[at+1];
                    if(alpha[0]>alpha[1])for(int j=2;j<8;j++)alpha[j]=((8-j)*alpha[0]+(j-1)*alpha[1])/7;
                    else {for(int j=2;j<6;j++)alpha[j]=((6-j)*alpha[0]+(j-1)*alpha[1])/5;alpha[6]=0;alpha[7]=255;}
                    for(int j=0;j<6;j++)alphaBits|=(ulong)data[at+2+j]<<(j*8);
                }
                for(int p=0;p<16;p++)
                {
                    int index=(int)(indices>>(2*p)&3);var color=palette[index];
                    int a=format==0x33545844?((data[at+p/2]>>(4*(p%2)))&15)*17:
                        format==0x35545844?alpha[(int)(alphaBits>>(3*p)&7)]:(!opaque && index==3?0:255);
                    int dest=((by*4+p/4)*256+bx*4+p%4)*4;
                    pixels[dest]=(byte)color.B;pixels[dest+1]=(byte)color.G;pixels[dest+2]=(byte)color.R;pixels[dest+3]=(byte)a;
                }
            }
        }
        var image=new Bitmap(256,256,PixelFormat.Format32bppArgb);
        var locked=image.LockBits(new(0,0,256,256),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
        try{for(int row=0;row<256;row++)Marshal.Copy(pixels,row*1024,IntPtr.Add(locked.Scan0,row*locked.Stride),1024);}
        finally{image.UnlockBits(locked);}
        return image;
    }
}
