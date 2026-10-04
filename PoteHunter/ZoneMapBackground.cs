using System.Drawing;
using System.Text.Json;
namespace PoteHunter;
sealed class ZoneMapBackground : IDisposable
{
 readonly record struct Manifest(int Zone,string Image,double MinX,double MinY,double MaxX,double MaxY);
 readonly record struct Loaded(Manifest Manifest,Bitmap Image);
 readonly Dictionary<int,Loaded?> cache=new(); readonly string root; readonly string? clientPath;
 bool? clientSupported;
 public ZoneMapBackground(string? root=null,string? clientPath=null){this.root=root??Path.Combine(AppContext.BaseDirectory,"maps");this.clientPath=clientPath??(root==null?PoteMemoryProbe.Program.ClientPath:null);}
 public bool TryGet(int zone,out Bitmap image,out (double MinX,double MinY,double MaxX,double MaxY) bounds){if(!cache.TryGetValue(zone,out var loaded))cache[zone]=loaded=Load(zone);if(loaded is { } value){image=value.Image;bounds=(value.Manifest.MinX,value.Manifest.MinY,value.Manifest.MaxX,value.Manifest.MaxY);return true;}image=null!;bounds=default;return false;}
 Loaded? Load(int zone)
 {
  if(zone<=0)return null;
  var path=Path.Combine(root,$"{zone}.json");
  if(File.Exists(path)) // Explicit calibrated maps always take precedence.
  {
   try{var m=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path));if(m.Zone!=zone||string.IsNullOrWhiteSpace(m.Image)||!double.IsFinite(m.MinX)||!double.IsFinite(m.MinY)||!double.IsFinite(m.MaxX)||!double.IsFinite(m.MaxY)||m.MaxX<=m.MinX||m.MaxY<=m.MinY)return null;var full=Path.GetFullPath(Path.Combine(root,m.Image));var rootFull=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;if(!full.StartsWith(rootFull,StringComparison.OrdinalIgnoreCase)||!File.Exists(full))return null;using var source=Image.FromFile(full);if(source.Width<32||source.Height<32)return null;return new Loaded(m,new Bitmap(source));}catch{return null;}
  }
  if(clientPath==null || GameMapLayout.Bounds(zone) is not {} bounds)return null;
  clientSupported??=GameMapLayout.ClientSupported(clientPath);
  if(clientSupported!=true)return null;
  var image=GameMapLayout.Load(Path.GetDirectoryName(clientPath)!,zone);
  return image==null?null:new Loaded(new(zone,"Local game DDS",bounds.MinX,bounds.MinY,bounds.MaxX,bounds.MaxY),image);
 }
 public void Dispose(){foreach(var value in cache.Values)value?.Image.Dispose();cache.Clear();}
 public static void SelfTest(){var root=Path.Combine(Path.GetTempPath(),"PoteHunter-zone-map-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);try{using(var b=new Bitmap(64,64))b.Save(Path.Combine(root,"zone.png"),System.Drawing.Imaging.ImageFormat.Png);File.WriteAllText(Path.Combine(root,"12.json"),"{\"Zone\":12,\"Image\":\"zone.png\",\"MinX\":0,\"MinY\":0,\"MaxX\":100,\"MaxY\":100}");using var maps=new ZoneMapBackground(root);if(!maps.TryGet(12,out _,out var bounds)||bounds.MaxX!=100)throw new Exception("Valid calibrated zone image was rejected.");}finally{try{Directory.Delete(root,true);}catch{}}}
}
