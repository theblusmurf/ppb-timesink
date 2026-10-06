namespace PoteHunter;

internal sealed record ProofPattern(string Name,string Hex,int[] RelocatedOperands)
{
 internal (byte[] Bytes,bool[] Mask) Parse()
 {
  string text=Hex.Replace(" ","");
  if(text.Length%2!=0)throw new InvalidDataException("Invalid instruction pattern.");
  var bytes=new byte[text.Length/2];var mask=new bool[bytes.Length];
  for(int i=0;i<bytes.Length;i++){string pair=text.Substring(i*2,2);if(pair=="??")mask[i]=true;else bytes[i]=Convert.ToByte(pair,16);}
  foreach(int offset in RelocatedOperands){if(offset<0||offset+4>bytes.Length)throw new InvalidDataException("Invalid relocation range.");for(int i=0;i<4;i++)mask[offset+i]=true;}
  return(bytes,mask);
 }
}
