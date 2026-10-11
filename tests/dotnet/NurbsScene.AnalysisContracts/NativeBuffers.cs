using System.Runtime.InteropServices;
using NurbsScene.Geometry;
using NurbsScene.Kernel;

internal static class NativeBuffers
{
    internal static IEnumerable<(string Name,Action Test)> Tests()
    {
        var native=new NativeNurbsEvaluator();var managed=new ManagedNurbsEvaluator();var f=new GeometryFactory(native);
        void Check(bool ok,string text="Native buffer assertion failed"){if(!ok)throw new Exception(text);}
        void Near(Vec3 a,Vec3 b)=>Check((a-b).Length<=1e-8*Math.Max(1,b.Length),$"{a} != {b}");
        void Throws<T>(Action run)where T:Exception{try{run();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
        yield return("native pinned layouts match C ABI scalar order",()=>{
            Check(Marshal.SizeOf<HPoint>()==32&&Marshal.SizeOf<Vec3>()==24);
            HPoint[] h=[new(1,2,3,4)];Vec3[] v=[new(5,6,7)];
            Check(MemoryMarshal.Cast<HPoint,double>(h).SequenceEqual(new double[]{1,2,3,4}));
            Check(MemoryMarshal.Cast<Vec3,double>(v).SequenceEqual(new double[]{5,6,7}));
        });
        yield return("native curve spans match managed derivatives and keep sentinels",()=>{
            var c=f.Circle(new(2,3,4),5);var output=new Vec3[9];
            foreach(var t in new[]{0d,.13,.25,.999,1}){
                output[^1]=new(99,98,97);native.EvaluateInto(c,t,7,output);var expected=managed.Evaluate(c,t,7);
                for(var i=0;i<8;i++)Near(output[i],expected[i]);Check(output[^1]==new Vec3(99,98,97));
            }
        });
        yield return("native surface spans preserve U-major mixed derivative order",()=>{
            var s=f.Saddle().ElevateDegree(SurfaceDirection.V,8);var output=new Vec3[21];output[^1]=new(9,8,7);
            native.EvaluateInto(s,.32,.67,3,4,output);var expected=managed.Evaluate(s,.32,.67,3,4);
            for(var i=0;i<4;i++)for(var j=0;j<5;j++)Near(output[i*5+j],expected[i,j]);Check(output[^1]==new Vec3(9,8,7));
        });
        yield return("native span validation leaves caller memory untouched",()=>{
            var c=f.Circle(Vec3.Zero,2);var s=f.Saddle();var output=Enumerable.Repeat(new Vec3(9,8,7),4).ToArray();
            Throws<ArgumentOutOfRangeException>(()=>native.EvaluateInto(c,double.NaN,1,output));
            Throws<ArgumentException>(()=>native.EvaluateInto(s,.5,.5,2,2,output));
            Throws<ArgumentOutOfRangeException>(()=>native.EvaluateInto(s,.5,.5,33,0,output));
            Throws<ArgumentOutOfRangeException>(()=>native.EvaluateInto(s,2,.5,1,1,output));
            Check(output.All(p=>p==new Vec3(9,8,7)));
        });
        yield return("warm native span calls allocate no managed memory",()=>{
            var c=f.Circle(Vec3.Zero,2);var s=f.Saddle().ElevateDegree(SurfaceDirection.U,6);var output=new Vec3[9];
            for(var i=0;i<100;i++){native.EvaluateInto(c,.31,2,output);native.EvaluateInto(s,.31,.73,2,2,output);}
            var before=GC.GetAllocatedBytesForCurrentThread();
            for(var i=0;i<1000;i++){native.EvaluateInto(c,.31,2,output);native.EvaluateInto(s,.31,.73,2,2,output);}
            var delta=GC.GetAllocatedBytesForCurrentThread()-before;Console.WriteLine($"METRIC native-span-2000-calls-bytes={delta}");Check(delta==0);
        });
        yield return("native spans and area analysis are concurrently reusable",()=>{
            var s=f.Extrude(f.Circle(Vec3.Zero,3),new(0,0,7));
            Parallel.For(0,16,i=>{Span<Vec3> output=stackalloc Vec3[9];native.EvaluateInto(s,.13,.77,2,2,output);Near(output[0],managed.Evaluate(s,.13,.77)[0,0]);});
            var r=s.GetAreaProperties();Check(r.Converged&&Math.Abs(r.Area-42*Math.PI)<1e-5);
        });
    }
}
